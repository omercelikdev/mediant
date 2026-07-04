namespace Mediant.Abstractions;

/// <summary>
/// A persisted notification awaiting reliable, at-least-once dispatch by the outbox processor.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Unique id of the message.</summary>
    public Guid Id { get; init; }

    /// <summary>Assembly-qualified type name of the notification, used to rehydrate it.</summary>
    public string NotificationType { get; init; } = string.Empty;

    /// <summary>Serialized notification payload (JSON).</summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>When the message was enqueued.</summary>
    public DateTimeOffset OccurredOn { get; init; }

    /// <summary>When the message was successfully dispatched, or null if still pending.</summary>
    public DateTimeOffset? ProcessedOn { get; set; }

    /// <summary>Number of dispatch attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>The last dispatch error, if any.</summary>
    public string? Error { get; set; }

    /// <summary>Identifier of the processor instance currently holding the dispatch claim, or null
    /// when unclaimed. Used by <see cref="IClaimingOutboxStore"/> for multi-instance coordination.</summary>
    public string? ClaimedBy { get; set; }

    /// <summary>When the current claim's lease expires. After this instant the message is
    /// reclaimable by any instance (the previous owner is assumed crashed). Null when unclaimed.</summary>
    public DateTimeOffset? ClaimedUntil { get; set; }
}

/// <summary>
/// Store for outbox messages. Persist messages in the SAME transaction as the business data so a
/// crash never loses an event; the outbox processor dispatches them after commit. Provide a durable
/// implementation (EF Core, SQL, …) in production.
/// </summary>
public interface IOutboxStore
{
    /// <summary>Persists a new outbox message.</summary>
    ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken);

    /// <summary>Returns up to <paramref name="batchSize"/> unprocessed messages, oldest first.</summary>
    ValueTask<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Marks a message as successfully dispatched.</summary>
    ValueTask MarkProcessedAsync(Guid id, DateTimeOffset processedOn, CancellationToken cancellationToken);

    /// <summary>Records a failed dispatch attempt.</summary>
    ValueTask MarkFailedAsync(Guid id, int attempts, string error, CancellationToken cancellationToken);
}

/// <summary>
/// An <see cref="IOutboxStore"/> that supports atomic claim-based dispatch for horizontally scaled
/// deployments: each processor instance claims a batch under a lease, so replicas polling the same
/// store dispatch each message once under normal operation. A crashed owner's messages become
/// reclaimable when the lease expires. The <c>OutboxProcessor</c> uses this automatically when the
/// registered store implements it; plain stores keep single-instance polling semantics.
/// </summary>
public interface IClaimingOutboxStore : IOutboxStore
{
    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> dispatchable messages (unprocessed,
    /// fewer than <paramref name="maxAttempts"/> attempts, unclaimed or lease-expired), oldest
    /// first, for <paramref name="ownerId"/> until now + <paramref name="leaseDuration"/>, and
    /// returns them. Messages claimed by another live owner are not returned.
    /// </summary>
    ValueTask<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        string ownerId, int batchSize, int maxAttempts, TimeSpan leaseDuration, CancellationToken cancellationToken);
}

/// <summary>
/// Enqueues notifications into the outbox for reliable dispatch. Inject this into handlers and call
/// it inside the business transaction instead of publishing directly.
/// </summary>
public interface IOutbox
{
    /// <summary>Serializes and persists a notification for later at-least-once dispatch.</summary>
    ValueTask EnqueueAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
