using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Attributes;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that retries requests on transient failures with exponential backoff and jitter.
/// Retries: TimeoutException, HttpRequestException, TaskCanceledException (non-user-cancelled).
/// Never retries: ValidationException, UnauthorizedAccessException, ArgumentException.
/// </summary>
public sealed class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 900;
    private readonly ILogger<RetryBehavior<TRequest, TResponse>> _logger;
    private readonly RetryBehaviorOptions _options;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly RetryableAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(RetryableAttribute), true)
            .Cast<RetryableAttribute>()
            .FirstOrDefault();

    // Exception types that should never be retried
    private static readonly HashSet<Type> NonRetryableExceptions = new()
    {
        typeof(UnauthorizedAccessException),
        typeof(ArgumentException),
        typeof(ArgumentNullException),
        typeof(ArgumentOutOfRangeException),
        typeof(InvalidOperationException),
        typeof(NotSupportedException),
        typeof(NotImplementedException)
    };

    public RetryBehavior(
        ILogger<RetryBehavior<TRequest, TResponse>> logger,
        IOptions<RetryBehaviorOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        if (CachedAttribute is not { } retryAttr)
        {
            return await next().ConfigureAwait(false);
        }

        var maxRetries = retryAttr.MaxRetryCount;

        // Max 0 — pass through
        if (maxRetries <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        var initialDelayMs = retryAttr.InitialDelayMs;
        var useExponentialBackoff = retryAttr.UseExponentialBackoff;
        var maxBackoffMs = _options.MaxBackoffCapSeconds * 1000;

        var attempt = 0;
        while (true)
        {
            try
            {
                var response = await next().ConfigureAwait(false);

                if (attempt > 0)
                {
                    _logger.LogInformation(
                        "Request {RequestName} succeeded on attempt {Attempt}/{MaxRetries}",
                        typeof(TRequest).Name, attempt + 1, maxRetries);
                }

                return response;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries, cancellationToken))
            {
                attempt++;
                var delay = CalculateDelay(attempt, initialDelayMs, useExponentialBackoff, maxBackoffMs);

                _logger.LogWarning(ex,
                    "Retry {Attempt}/{MaxRetries} for {RequestName} after {DelayMs}ms. Error: {Error}",
                    attempt, maxRetries, typeof(TRequest).Name, delay, ex.Message);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancel during wait — stop retrying
                    throw;
                }
            }
        }
    }

    private static bool ShouldRetry(Exception ex, int attempt, int maxRetries, CancellationToken cancellationToken)
    {
        if (attempt >= maxRetries)
        {
            return false;
        }

        // Never retry user-initiated cancellation
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        // Never retry non-retryable exceptions
        var exType = ex.GetType();
        if (NonRetryableExceptions.Contains(exType))
        {
            return false;
        }

        // Check for validation-type exceptions by name (FluentValidation etc.)
        if (exType.Name.Contains("Validation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Retryable: TimeoutException, HttpRequestException, TaskCanceledException (non-user)
        // Note: TaskCanceledException inherits from OperationCanceledException, so we check
        // whether the exception's token differs from the user's token (HttpClient timeouts
        // set CancellationToken.None, not the user's token).
        return ex is TimeoutException or HttpRequestException
            || (ex is TaskCanceledException tce && tce.CancellationToken != cancellationToken);
    }

    private static int CalculateDelay(int attempt, int initialDelayMs, bool useExponentialBackoff, int maxBackoffMs)
    {
        if (initialDelayMs <= 0)
        {
            initialDelayMs = 1;
        }

        long baseDelay;
        if (useExponentialBackoff)
        {
            // Cap the shift exponent so 2^(attempt-1) cannot overflow Int32 (which previously
            // produced a negative baseDelay and an ArgumentOutOfRangeException in the jitter call).
            var exponent = Math.Min(attempt - 1, 30);
            baseDelay = (long)initialDelayMs * (1L << exponent);
        }
        else
        {
            baseDelay = initialDelayMs;
        }

        // Cap before applying jitter so the jitter range stays bounded.
        if (maxBackoffMs > 0)
        {
            baseDelay = Math.Min(baseDelay, maxBackoffMs);
        }

        // Add jitter (±25%), guarding against an empty range for small delays.
        var jitterMagnitude = (int)(baseDelay / 4);
        var jitter = jitterMagnitude > 0 ? Random.Shared.Next(-jitterMagnitude, jitterMagnitude) : 0;
        var delay = baseDelay + jitter;

        if (delay < 0)
        {
            delay = 0;
        }
        if (maxBackoffMs > 0 && delay > maxBackoffMs)
        {
            delay = maxBackoffMs;
        }

        return (int)delay;
    }
}
