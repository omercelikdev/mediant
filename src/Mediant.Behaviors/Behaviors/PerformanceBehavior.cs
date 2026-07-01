using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that monitors request execution time using Stopwatch.
/// Logs warnings for slow requests and critical alerts for very slow requests.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 800;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly PerformanceThresholdAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(PerformanceThresholdAttribute), true)
            .Cast<PerformanceThresholdAttribute>()
            .FirstOrDefault();

    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger;
    private readonly PerformanceBehaviorOptions _options;

    public PerformanceBehavior(
        ILogger<PerformanceBehavior<TRequest, TResponse>> logger,
        IOptions<PerformanceBehaviorOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (_options.WarningThresholdMs <= 0)
        {
            throw new ArgumentException(
                "WarningThresholdMs must be positive.", nameof(options));
        }
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            return await next().ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            // Evaluate thresholds even when the handler throws, so slow failing requests are
            // still reported.
            EvaluateThresholds(sw.Elapsed.TotalMilliseconds);
        }
    }

    private void EvaluateThresholds(double elapsedMs)
    {
        // Under 0.01ms — don't log (too fast to be meaningful)
        if (elapsedMs < 0.01)
        {
            return;
        }

        var requestName = typeof(TRequest).Name;

        var warningMs = CachedAttribute is not null && CachedAttribute.WarningMs > 0
            ? CachedAttribute.WarningMs
            : _options.WarningThresholdMs;
        var criticalMs = CachedAttribute is not null && CachedAttribute.CriticalMs > 0
            ? CachedAttribute.CriticalMs
            : _options.CriticalThresholdMs;

        // Over 30 seconds — always critical regardless of thresholds
        if (elapsedMs > 30_000)
        {
            _logger.LogCritical(
                "CRITICAL: {RequestName} took {ElapsedMs}ms (over 30s threshold)",
                requestName, elapsedMs);
        }
        else if (elapsedMs > criticalMs)
        {
            _logger.LogError(
                "SLOW: {RequestName} took {ElapsedMs}ms (critical threshold: {Threshold}ms)",
                requestName, elapsedMs, criticalMs);
        }
        else if (elapsedMs > warningMs)
        {
            _logger.LogWarning(
                "SLOW: {RequestName} took {ElapsedMs}ms (warning threshold: {Threshold}ms)",
                requestName, elapsedMs, warningMs);
        }
    }
}
