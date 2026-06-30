using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Behaviors.Outbox;

/// <summary>
/// Default <see cref="IOutbox"/> — serializes a notification and persists it via the
/// <see cref="IOutboxStore"/>. Enqueue inside the business transaction so the message commits
/// atomically with the data.
/// </summary>
public sealed class DefaultOutbox : IOutbox
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private readonly IOutboxStore _store;

    /// <summary>Initializes a new instance of <see cref="Outbox"/>.</summary>
    public DefaultOutbox(IOutboxStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Outbox serializes notifications with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("Outbox serializes notifications with reflection-based System.Text.Json.")]
    public ValueTask EnqueueAsync<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);

        var runtimeType = notification.GetType();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            NotificationType = runtimeType.AssemblyQualifiedName ?? runtimeType.FullName ?? runtimeType.Name,
            Payload = JsonSerializer.Serialize(notification, runtimeType, SerializerOptions),
            OccurredOn = DateTimeOffset.UtcNow,
        };

        return _store.AddAsync(message, cancellationToken);
    }
}
