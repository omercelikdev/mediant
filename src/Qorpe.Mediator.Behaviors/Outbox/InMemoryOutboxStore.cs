using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Behaviors.Outbox;

/// <summary>
/// In-memory <see cref="IOutboxStore"/> for development and testing. Thread-safe but NOT durable —
/// use a database-backed store (EF Core, SQL, …) in production so messages survive a crash.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly object _gate = new();
    private readonly List<OutboxMessage> _messages = new();

    /// <inheritdoc />
    public ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _messages.Add(message);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<OutboxMessage> batch = _messages
                .Where(m => m.ProcessedOn is null)
                .OrderBy(m => m.OccurredOn)
                .Take(batchSize)
                .ToList();
            return new ValueTask<IReadOnlyList<OutboxMessage>>(batch);
        }
    }

    /// <inheritdoc />
    public ValueTask MarkProcessedAsync(Guid id, DateTimeOffset processedOn, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id);
            if (message is not null)
            {
                message.ProcessedOn = processedOn;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask MarkFailedAsync(Guid id, int attempts, string error, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id);
            if (message is not null)
            {
                message.Attempts = attempts;
                message.Error = error;
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Gets a snapshot of all messages. For testing.</summary>
    public IReadOnlyList<OutboxMessage> GetAll()
    {
        lock (_gate)
        {
            return _messages.ToList();
        }
    }
}
