using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that invalidates cache entries after successful command execution.
/// Triggered by [InvalidatesCache("prefix")] attribute on command types.
/// Executes after the handler succeeds — never invalidates on failure.
/// </summary>
public sealed class CacheInvalidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 1001; // Just after CachingBehavior (1000)

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly InvalidatesCacheAttribute[] CachedAttributes =
        typeof(TRequest).GetCustomAttributes(typeof(InvalidatesCacheAttribute), true)
            .Cast<InvalidatesCacheAttribute>()
            .ToArray();

    private readonly ICacheInvalidator? _cacheInvalidator;
    private readonly ILogger<CacheInvalidationBehavior<TRequest, TResponse>> _logger;

    // Ensures the "no invalidator registered" warning is emitted once per closed generic type
    // instead of on every command execution.
    private static int _missingInvalidatorWarned;

    public CacheInvalidationBehavior(
        ILogger<CacheInvalidationBehavior<TRequest, TResponse>> logger,
        ICacheInvalidator? cacheInvalidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheInvalidator = cacheInvalidator;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (CachedAttributes.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        if (_cacheInvalidator is null)
        {
            // [InvalidatesCache] is present but nothing can act on it — a silent no-op would leave
            // stale data with no signal, so warn once. Register an ICacheInvalidator (AddMediantCaching
            // ships one by default) to enable invalidation.
            if (Interlocked.Exchange(ref _missingInvalidatorWarned, 1) == 0)
            {
                _logger.LogWarning(
                    "{RequestName} declares [InvalidatesCache] but no ICacheInvalidator is registered; cache invalidation is a no-op.",
                    typeof(TRequest).Name);
            }

            return await next().ConfigureAwait(false);
        }

        // Execute the handler first
        var response = await next().ConfigureAwait(false);

        // Invalidate cache entries after successful execution
        for (int i = 0; i < CachedAttributes.Length; i++)
        {
            try
            {
                await _cacheInvalidator.InvalidateAsync(CachedAttributes[i].KeyPrefix, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Invalidated cache entries with prefix '{Prefix}' after {RequestName}",
                    CachedAttributes[i].KeyPrefix, typeof(TRequest).Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cache invalidation failed for prefix '{Prefix}' after {RequestName}",
                    CachedAttributes[i].KeyPrefix, typeof(TRequest).Name);
            }
        }

        return response;
    }
}
