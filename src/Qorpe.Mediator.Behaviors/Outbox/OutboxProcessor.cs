using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Behaviors.Outbox;

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

    /// <summary>Initializes a new instance of <see cref="OutboxProcessor"/>.</summary>
    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxProcessorOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var pending = await store.GetUnprocessedAsync(_options.BatchSize, cancellationToken).ConfigureAwait(false);

        for (int i = 0; i < pending.Count; i++)
        {
            var message = pending[i];
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
