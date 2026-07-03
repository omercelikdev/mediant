using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;

namespace Mediant.Behaviors.Outbox;

/// <summary>Options for the <see cref="OutboxProcessor"/>.</summary>
public sealed class OutboxProcessorOptions
{
    /// <summary>How often to poll the store for pending messages. Default 5 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum messages dispatched per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Maximum dispatch attempts before a message is left as failed. Default 5.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long a claimed batch stays leased to one processor instance when the store implements
    /// <see cref="IClaimingOutboxStore"/>. A crashed owner's messages become reclaimable after the
    /// lease expires, so it must comfortably exceed the worst-case batch dispatch time.
    /// Default 60 seconds.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Stable identifier for this processor instance in claim ownership. When null (default) a
    /// unique id derived from the machine name is generated per processor lifetime.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used to (de)serialize outbox payloads.
    /// Set this to options backed by a <c>JsonSerializerContext</c> for trimming/Native AOT.
    /// When null, reflection-based defaults are used.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }
}

/// <summary>
/// Background service that drains the <see cref="IOutboxStore"/>, rehydrates each notification and
/// publishes it through the mediator (at-least-once). Failed dispatches are retried up to
/// <see cref="OutboxProcessorOptions.MaxAttempts"/>.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxProcessorOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly string _ownerId;

    // Ids whose abandonment has already been logged, so a poison message that stays in the store
    // is reported once per process lifetime instead of on every poll. Guarded because
    // ProcessPendingAsync is public for deterministic testing.
    private readonly HashSet<Guid> _abandonmentLogged = new();

    /// <summary>Initializes a new instance of <see cref="OutboxProcessor"/>.</summary>
    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ownerId = _options.OwnerId ?? $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Outbox processing loop failed; will retry next interval");
            }

            try
            {
                await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Processes one batch of pending messages. Exposed for deterministic testing.
    /// </summary>
    [RequiresUnreferencedCode("Outbox rehydrates notifications with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("Outbox rehydrates notifications with reflection-based System.Text.Json.")]
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        // Claiming stores coordinate multi-instance dispatch: each replica atomically leases its
        // batch, so two processors on the same store do not double-dispatch. Plain stores keep the
        // single-instance polling semantics.
        var pending = store is IClaimingOutboxStore claiming
            ? await claiming.ClaimPendingAsync(_ownerId, _options.BatchSize, _options.MaxAttempts, _options.LeaseDuration, cancellationToken).ConfigureAwait(false)
            : await store.GetUnprocessedAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < pending.Count; i++)
        {
            var message = pending[i];

            if (message.Attempts >= _options.MaxAttempts)
            {
                bool firstTime;
                lock (_abandonmentLogged)
                {
                    firstTime = _abandonmentLogged.Add(message.Id);
                }

                if (firstTime)
                {
                    _logger.LogError(
                        "Outbox message {MessageId} abandoned after {Attempts} attempts (MaxAttempts: {MaxAttempts}); it will not be retried. Last error: {Error}",
                        message.Id, message.Attempts, _options.MaxAttempts, message.Error);
                }

                continue;
            }

            try
            {
                var notification = Rehydrate(message, _options.SerializerOptions ?? DefaultOutbox.DefaultSerializerOptions);
                await publisher.Publish(notification, cancellationToken).ConfigureAwait(false);
                await store.MarkProcessedAsync(message.Id, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var attempts = message.Attempts + 1;
                _logger.LogError(ex, "Outbox dispatch failed for message {MessageId} (attempt {Attempts}/{Max})",
                    message.Id, attempts, _options.MaxAttempts);
                await store.MarkFailedAsync(message.Id, attempts, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [RequiresUnreferencedCode("Outbox rehydrates notifications with reflection-based System.Text.Json.")]
    [RequiresDynamicCode("Outbox rehydrates notifications with reflection-based System.Text.Json.")]
    private static INotification Rehydrate(OutboxMessage message, JsonSerializerOptions serializerOptions)
    {
        var type = Type.GetType(message.NotificationType)
            ?? throw new InvalidOperationException(
                $"Outbox message {message.Id} references unknown notification type '{message.NotificationType}'.");

        if (JsonSerializer.Deserialize(message.Payload, type, serializerOptions) is not INotification notification)
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} payload did not deserialize to an INotification.");
        }

        return notification;
    }
}
