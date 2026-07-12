using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Non-generic holder for transaction scope state. Shared across all closed generic
/// TransactionBehavior types so nested dispatch (OuterCommand → InnerCommand) is detected.
/// </summary>
internal static class TransactionScope
{
    internal static readonly AsyncLocal<bool> IsInTransaction = new();

    // Nesting depth below the transaction owner; used to derive unique savepoint names when
    // TransactionBehaviorOptions.NestedSavepoints is enabled.
    internal static readonly AsyncLocal<int> Depth = new();
}

/// <summary>
/// Pipeline behavior that wraps command execution in a transaction.
/// Automatically skips queries. Supports rollback. Nested dispatch joins the ambient
/// transaction by default; enable <see cref="TransactionBehaviorOptions.NestedSavepoints"/> to
/// unwind a failed nested command via savepoints instead, so an outer handler can catch the
/// failure and commit without the inner command's writes.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 700;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly TransactionalAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(TransactionalAttribute), true)
            .Cast<TransactionalAttribute>()
            .FirstOrDefault();

    // Cached type check — runs once per closed generic type, not per request
    private static readonly bool IsQueryType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

    // Shared across all closed generic types — must be non-generic to work for nested dispatch
    // where OuterCommand and InnerCommand produce different closed types.
    private static readonly AsyncLocal<bool> IsInTransaction = TransactionScope.IsInTransaction;

    private readonly IUnitOfWork? _unitOfWork;
    private readonly IPostCommitTaskQueue? _postCommitQueue;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;
    private readonly TransactionBehaviorOptions _options;

    public TransactionBehavior(
        ILogger<TransactionBehavior<TRequest, TResponse>> logger,
        IOptions<TransactionBehaviorOptions> options,
        IUnitOfWork? unitOfWork = null,
        IPostCommitTaskQueue? postCommitQueue = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _unitOfWork = unitOfWork;
        _postCommitQueue = postCommitQueue;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        // Skip queries — transactions are for commands only
        if (IsQueryType)
        {
            return await next().ConfigureAwait(false);
        }

        if (CachedAttribute is null)
        {
            return await next().ConfigureAwait(false);
        }

        if (_unitOfWork is null)
        {
            throw new InvalidOperationException(
                $"IUnitOfWork is required for transactional request '{typeof(TRequest).Name}'. " +
                "Register an IUnitOfWork implementation in the DI container.");
        }

        // Nested transaction: the outermost behavior owns the transaction. By default the nested
        // command just participates; with NestedSavepoints it gets its own savepoint so a failure
        // unwinds only its work.
        if (IsInTransaction.Value)
        {
            if (!_options.NestedSavepoints)
            {
                _logger.LogDebug("Joining existing transaction for nested {RequestName}", typeof(TRequest).Name);
                return await next().ConfigureAwait(false);
            }

            return await HandleNestedWithSavepointAsync(next, cancellationToken).ConfigureAwait(false);
        }

        var requestName = typeof(TRequest).Name;
        _logger.LogDebug("Beginning transaction for {RequestName}", requestName);

        IsInTransaction.Value = true;
        await _unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        TResponse response;

        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler failed for {RequestName}, rolling back transaction", requestName);
            await SafeRollbackAsync(requestName, cancellationToken).ConfigureAwait(false);
            IsInTransaction.Value = false;
            throw;
        }

        try
        {
            // Flush pending changes before commit (EF Core SaveChanges safety net)
            await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _unitOfWork.CommitAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Committed transaction for {RequestName}", requestName);
            IsInTransaction.Value = false;

            // Execute post-commit tasks after successful commit (outside transaction scope)
            if (_postCommitQueue is not null)
            {
                await _postCommitQueue.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction commit failed for {RequestName}, rolling back", requestName);
            await SafeRollbackAsync(requestName, cancellationToken).ConfigureAwait(false);
            IsInTransaction.Value = false;
            throw;
        }
    }

    private async ValueTask<TResponse> HandleNestedWithSavepointAsync(
        RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var depth = TransactionScope.Depth.Value + 1;
        TransactionScope.Depth.Value = depth;
        var savepoint = $"mediant_sp_{depth}";

        try
        {
            // Flush the outer scope's pending changes first so the savepoint separates outer
            // work (kept) from inner work (unwound on failure).
            await _unitOfWork!.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _unitOfWork.CreateSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Created savepoint {Savepoint} for nested {RequestName}", savepoint, requestName);

            try
            {
                var response = await next().ConfigureAwait(false);

                // Flush the inner command's work while still inside its savepoint window so a
                // shallower rollback-to-savepoint covers it too.
                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nested {RequestName} failed, rolling back to savepoint {Savepoint}",
                    requestName, savepoint);

                // Best-effort flush so tracked-but-unflushed inner changes land inside the
                // savepoint window and are undone below. If the flush itself fails (often the
                // very constraint violation that brought us here), the dirty entities stay in
                // the tracker and the OUTER commit's SaveChanges fails → full rollback. Fail-safe
                // either way — never a silent partial commit.
                try
                {
                    await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception flushEx)
                {
                    _logger.LogDebug(flushEx,
                        "Could not flush changes of failed nested {RequestName}; outer commit will surface them",
                        requestName);
                }

                await _unitOfWork.RollbackToSavepointAsync(savepoint, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            TransactionScope.Depth.Value = depth - 1;
        }
    }

    private async ValueTask SafeRollbackAsync(string requestName, CancellationToken cancellationToken)
    {
        // Drop any post-commit tasks the (now rolled-back) handler enqueued so their side
        // effects never run — including for a later committing command in the same DI scope.
        _postCommitQueue?.Clear();

        try
        {
            await _unitOfWork!.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception rollbackEx)
        {
            _logger.LogCritical(rollbackEx,
                "CRITICAL: Rollback failed for {RequestName}. Original exception preserved.",
                requestName);
        }
    }

}
