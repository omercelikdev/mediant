using Microsoft.Extensions.Caching.Hybrid;
using Mediant.Abstractions;

namespace Mediant.Behaviors.Caching;

/// <summary>
/// <see cref="ICacheInvalidator"/> for the <see cref="HybridCache"/> caching path. A prefix maps to
/// a HybridCache tag, so <c>[InvalidatesCache("prefix")]</c> is an exact, O(1)
/// <c>HybridCache.RemoveByTagAsync</c> — no key registry or prefix scan. Registered by
/// <c>AddMediantHybridCaching()</c>.
/// </summary>
public sealed class HybridCacheInvalidator : ICacheInvalidator
{
    // Reserved tag used by AddMediantHybridCaching so InvalidateAllAsync has a single tag to clear.
    internal const string AllEntriesTag = "mediant:hybrid:all";

    private readonly HybridCache _cache;

    /// <summary>Initializes a new instance of <see cref="HybridCacheInvalidator"/>.</summary>
    public HybridCacheInvalidator(HybridCache cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAsync(string keyPrefix, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPrefix);
        await _cache.RemoveByTagAsync(keyPrefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default)
        => await _cache.RemoveByTagAsync(AllEntriesTag, cancellationToken).ConfigureAwait(false);
}
