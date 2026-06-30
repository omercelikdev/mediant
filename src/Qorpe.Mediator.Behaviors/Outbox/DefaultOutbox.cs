using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Behaviors.Outbox;

/// <summary>
/// Default <see cref="IOutbox"/> — serializes a notification and persists it via the
/// <see cref="IOutboxStore"/>. Enqueue inside the business transaction so the message commits
/// atomically with the data.
/// </summary>
public sealed class DefaultOutbox : IOutbox
{
    internal static readonly JsonSerializerOptions DefaultSerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private readonly IOutboxStore _store;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>Initializes a new instance of <see cref="DefaultOutbox"/>.</summary>
    public DefaultOutbox(IOutboxStore store, IOptions<OutboxProcessorOptions>? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializerOptions = options?.Value.SerializerOptions ?? DefaultSerializerOptions;
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
            Payload = JsonSerializer.Serialize(notification, runtimeType, _serializerOptions),
            OccurredOn = DateTimeOffset.UtcNow,
        };

        return _store.AddAsync(message, cancellationToken);
    }
}
