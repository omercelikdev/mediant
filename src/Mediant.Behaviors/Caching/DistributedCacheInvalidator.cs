using Microsoft.Extensions.Caching.Distributed;
using Mediant.Abstractions;

namespace Mediant.Behaviors.Caching;

/// <summary>
/// Default <see cref="ICacheInvalidator"/> for the <see cref="IDistributedCache"/> caching path.
/// Because a distributed cache cannot enumerate keys, it removes the keys an
/// <see cref="ICacheKeyRegistry"/> recorded under each prefix (populated by <c>CachingBehavior</c>
/// as it writes cache entries). Registered by <c>AddMediantCaching</c>, so <c>[InvalidatesCache]</c>
/// works out of the box.
/// </summary>
public sealed class DistributedCacheInvalidator : ICacheInvalidator
{
    private readonly IDistributedCache? _cache;
    private readonly ICacheKeyRegistry _registry;

    /// <summary>Initializes a new instance of <see cref="DistributedCacheInvalidator"/>.</summary>
    public DistributedCacheInvalidator(ICacheKeyRegistry registry, IDistributedCache? cache = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _cache = cache;
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
        if (_cache is null)
        {
            return;
        }

        var keys = await _registry.GetKeysAsync(keyPrefix, cancellationToken).ConfigureAwait(false);
        foreach (var key in keys)
        {
            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }

        await _registry.ClearAsync(keyPrefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is null)
        {
            return;
        }

        var prefixes = await _registry.GetPrefixesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var prefix in prefixes)
        {
            await InvalidateAsync(prefix, cancellationToken).ConfigureAwait(false);
        }
    }
}
