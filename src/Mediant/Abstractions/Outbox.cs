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
/// Enqueues notifications into the outbox for reliable dispatch. Inject this into handlers and call
/// it inside the business transaction instead of publishing directly.
/// </summary>
public interface IOutbox
{
    /// <summary>Serializes and persists a notification for later at-least-once dispatch.</summary>
    ValueTask EnqueueAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
