using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;

namespace Mediant.Behaviors.Audit;

/// <summary>
/// Background service that periodically drains the <see cref="AuditBuffer"/> into
/// <typeparamref name="TInner"/> in batches. Graceful shutdown performs a final flush so buffered
/// entries are not lost on ordinary application stop.
/// </summary>
/// <typeparam name="TInner">The durable store that receives the batched writes.</typeparam>
public sealed class AuditFlushProcessor<TInner> : BackgroundService
    where TInner : IAuditStore
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditBuffer _buffer;
    private readonly AuditBufferOptions _options;
    private readonly ILogger<AuditFlushProcessor<TInner>> _logger;

    /// <summary>Initializes a new instance of <see cref="AuditFlushProcessor{TInner}"/>.</summary>
    public AuditFlushProcessor(
        IServiceScopeFactory scopeFactory,
        AuditBuffer buffer,
        IOptions<AuditBufferOptions> options,
        ILogger<AuditFlushProcessor<TInner>> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
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
                await Task.Delay(_options.FlushInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await FlushOnceAsync(stoppingToken).ConfigureAwait(false);
        }

        // Graceful shutdown: persist whatever is still buffered.
        await FlushOnceAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task FlushOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var inner = scope.ServiceProvider.GetRequiredService<TInner>();
            await _buffer.DrainAsync(inner, _options.BatchSize, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Audit buffer flush failed; entries stay buffered for the next attempt");
        }
    }
}
