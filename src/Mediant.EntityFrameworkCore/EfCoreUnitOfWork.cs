using Microsoft.EntityFrameworkCore;
using Mediant.Abstractions;

namespace Mediant.EntityFrameworkCore;

/// <summary>
/// <see cref="IUnitOfWork"/> backed by an EF Core <typeparamref name="TContext"/>, wiring
/// <c>[Transactional]</c> to <c>Database.BeginTransaction/Commit/Rollback</c> on the same scoped
/// context your handlers write through — so handler changes, tracked outbox messages and the
/// commit are atomic without any repository layer.
/// <para>
/// <see cref="BeginTransactionAsync"/> is a no-op when a transaction is already open (e.g. an
/// execution-strategy retry re-entering the behavior), and <see cref="RollbackAsync"/> clears the
/// change tracker so entities from the rolled-back handler can never be flushed by a later
/// <c>SaveChanges</c> on the same context (such as an audit write after a failure).
/// </para>
/// <para>
/// On non-relational providers (e.g. EF InMemory in tests) transaction operations are no-ops;
/// <see cref="SaveChangesAsync"/> still flushes, so <c>[Transactional]</c> handlers stay testable.
/// </para>
/// </summary>
public sealed class EfCoreUnitOfWork<TContext> : IUnitOfWork
    where TContext : DbContext
{
    private readonly TContext _context;

    /// <summary>Initializes a new instance of <see cref="EfCoreUnitOfWork{TContext}"/>.</summary>
    public EfCoreUnitOfWork(TContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return;
        }

        // Already inside a transaction (execution-strategy retry, or one the caller opened) —
        // participate instead of double-beginning.
        if (_context.Database.CurrentTransaction is not null)
        {
            return;
        }

        await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveChangesAsync(CancellationToken cancellationToken)
        => await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await transaction.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction;

        try
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            // Rollback does not detach tracked entities; clear them so a later SaveChanges on
            // this context (e.g. a failure-audit write) cannot flush the rolled-back handler's
            // Added/Modified entities outside the transaction.
            _context.ChangeTracker.Clear();
        }
    }

    /// <inheritdoc />
    public async ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        await transaction.CreateSavepointAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
    {
        var transaction = _context.Database.CurrentTransaction;
        if (transaction is null)
        {
            return;
        }

        await transaction.RollbackToSavepointAsync(name, cancellationToken).ConfigureAwait(false);
    }
}
