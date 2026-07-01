using Mediant.Audit;

namespace Mediant.Abstractions;

/// <summary>
/// Defines the store for persisting audit entries.
/// </summary>
public interface IAuditStore
{
    /// <summary>
    /// Saves an audit entry.
    /// </summary>
    /// <param name="entry">The audit entry to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a batch of audit entries.
    /// </summary>
    /// <param name="entries">The audit entries to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Queries audit entries.
    /// </summary>
    /// <param name="query">The query parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching audit entries.</returns>
    ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the context for retrieving audit user information.
/// </summary>
public interface IAuditUserContext
{
    /// <summary>
    /// Gets the current user identifier.
    /// </summary>
    /// <returns>The user identifier, or null if not available.</returns>
    string? GetUserId();

    /// <summary>
    /// Gets the current user name.
    /// </summary>
    /// <returns>The user name, or null if not available.</returns>
    string? GetUserName();
}

/// <summary>
/// Defines the unit of work abstraction for transaction support.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask CommitAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back the current transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask RollbackAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a savepoint within the current transaction for nested transaction support.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Rolls back to a savepoint within the current transaction.
    /// </summary>
    /// <param name="name">The savepoint name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Flushes pending changes to the database before transaction commit.
    /// EF Core implementations should call DbContext.SaveChangesAsync here.
    /// Default implementation is a no-op for backward compatibility.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask SaveChangesAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>
/// Defines a queue for fire-and-forget tasks that execute after a transaction commits.
/// Register as Scoped in DI. Handlers enqueue tasks during execution, and
/// TransactionBehavior calls ExecuteAsync after successful commit.
/// </summary>
public interface IPostCommitTaskQueue
{
    /// <summary>
    /// Enqueues a task to run after the transaction commits.
    /// </summary>
    /// <param name="task">The async task to execute post-commit.</param>
    void Enqueue(Func<CancellationToken, Task> task);

    /// <summary>
    /// Executes all queued tasks sequentially. Failures are logged but do not throw.
    /// Called by TransactionBehavior after successful commit.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Discards all queued tasks without executing them. Called by TransactionBehavior when the
    /// transaction rolls back, so post-commit side effects never fire for rolled-back work.
    /// </summary>
    void Clear();
}

/// <summary>
/// Defines the idempotency store for checking duplicate requests.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Checks if a request with the given key has already been processed.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the request exists; otherwise, false.</returns>
    ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the cached response for a previously processed request.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached response, or default if not found.</returns>
    ValueTask<TResponse?> GetAsync<TResponse>(string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the response for a processed request.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <param name="response">The response to cache.</param>
    /// <param name="window">The time window for idempotency.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask SetAsync<TResponse>(string idempotencyKey, TResponse response, TimeSpan window, CancellationToken cancellationToken);

    /// <summary>
    /// Removes a stored idempotency entry.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask RemoveAsync(string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>
/// Defines the authorization context for checking permissions.
/// </summary>
public interface IAuthorizationContext
{
    /// <summary>
    /// Gets the current user identifier.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the roles assigned to the current user.
    /// </summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Gets whether the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Checks if the current user has a specific claim.
    /// </summary>
    /// <param name="claimType">The claim type.</param>
    /// <param name="claimValue">The claim value.</param>
    /// <returns>True if the user has the claim; otherwise, false.</returns>
    bool HasClaim(string claimType, string claimValue);
}
