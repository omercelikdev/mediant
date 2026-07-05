namespace Mediant.Behaviors.Caching;

/// <summary>
/// Tracks which cache keys were written under each <c>[Cacheable(CacheKeyPrefix = ...)]</c> prefix
/// so a prefix-based <see cref="Mediant.Abstractions.ICacheInvalidator"/> can remove them —
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> cannot enumerate keys.
/// <para>
/// The default implementation stores the registry in the same distributed cache and is
/// <b>best-effort</b> across processes: concurrent writers under one prefix can race the
/// read-modify-write of the key set, so a rare orphaned key survives until its own TTL expires.
/// This bounds worst-case staleness to the cache entry TTL. A HybridCache tag-based path (see the
/// caching docs) makes invalidation exact and O(1).
/// </para>
/// </summary>
public interface ICacheKeyRegistry
{
    /// <summary>Records that <paramref name="key"/> was written under <paramref name="prefix"/>.
    /// <paramref name="ttl"/> keeps the registry entry alive at least as long as the cache entry.</summary>
    ValueTask RegisterAsync(string prefix, string key, TimeSpan ttl, CancellationToken cancellationToken);

    /// <summary>Returns the keys currently registered under <paramref name="prefix"/>.</summary>
    ValueTask<IReadOnlyCollection<string>> GetKeysAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>Returns all prefixes that currently have registered keys.</summary>
    ValueTask<IReadOnlyCollection<string>> GetPrefixesAsync(CancellationToken cancellationToken);

    /// <summary>Removes the key set recorded for <paramref name="prefix"/>.</summary>
    ValueTask ClearAsync(string prefix, CancellationToken cancellationToken);
}
