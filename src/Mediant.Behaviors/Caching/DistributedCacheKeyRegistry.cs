using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Caching;

/// <summary>
/// Default <see cref="ICacheKeyRegistry"/> backed by the same <see cref="IDistributedCache"/> the
/// caching pipeline uses. Per-prefix key sets and the set of known prefixes are stored as JSON.
/// Read-modify-write is serialized per prefix with an in-process lock pool to keep intra-process
/// updates correct; cross-process races are best-effort (documented on <see cref="ICacheKeyRegistry"/>).
/// </summary>
public sealed class DistributedCacheKeyRegistry : ICacheKeyRegistry
{
    private const string KeySetPrefix = "mediant:cache-registry:keys:";
    private const string PrefixSetKey = "mediant:cache-registry:prefixes";

    // The prefix registry must outlive the cache entries it tracks; keep it comfortably longer than
    // any single entry TTL so it does not expire mid-window and lose invalidation coverage.
    private static readonly TimeSpan MinRegistryTtl = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private readonly IDistributedCache? _cache;
    private readonly BoundedLockPool _locks;

    /// <summary>Initializes a new instance of <see cref="DistributedCacheKeyRegistry"/>.</summary>
    public DistributedCacheKeyRegistry(IOptions<CachingBehaviorOptions> options, IDistributedCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _cache = cache;
        _locks = new BoundedLockPool(options.Value.MaxLockPoolSize);
    }

    /// <inheritdoc />
    public async ValueTask RegisterAsync(string prefix, string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (_cache is null)
        {
            return;
        }

        var registryTtl = ttl > MinRegistryTtl ? ttl : MinRegistryTtl;

        using var keyLock = await _locks.AcquireAsync(KeySetPrefix + prefix, cancellationToken).ConfigureAwait(false);

        var keys = await ReadSetAsync(KeySetPrefix + prefix, cancellationToken).ConfigureAwait(false);
        if (keys.Add(key))
        {
            await WriteSetAsync(KeySetPrefix + prefix, keys, registryTtl, cancellationToken).ConfigureAwait(false);
        }

        await RegisterPrefixAsync(prefix, registryTtl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<string>> GetKeysAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (_cache is null)
        {
            return [];
        }

        return await ReadSetAsync(KeySetPrefix + prefix, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<string>> GetPrefixesAsync(CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return [];
        }

        return await ReadSetAsync(PrefixSetKey, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ClearAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (_cache is null)
        {
            return;
        }

        using var keyLock = await _locks.AcquireAsync(KeySetPrefix + prefix, cancellationToken).ConfigureAwait(false);
        await _cache.RemoveAsync(KeySetPrefix + prefix, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RegisterPrefixAsync(string prefix, TimeSpan ttl, CancellationToken cancellationToken)
    {
        using var prefixLock = await _locks.AcquireAsync(PrefixSetKey, cancellationToken).ConfigureAwait(false);

        var prefixes = await ReadSetAsync(PrefixSetKey, cancellationToken).ConfigureAwait(false);
        if (prefixes.Add(prefix))
        {
            await WriteSetAsync(PrefixSetKey, prefixes, ttl, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<HashSet<string>> ReadSetAsync(string storeKey, CancellationToken cancellationToken)
    {
        var bytes = await _cache!.GetAsync(storeKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var values = JsonSerializer.Deserialize<List<string>>(bytes, SerializerOptions);
        return values is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(values, StringComparer.Ordinal);
    }

    private async ValueTask WriteSetAsync(string storeKey, HashSet<string> set, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(set, SerializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
        await _cache!.SetAsync(storeKey, bytes, options, cancellationToken).ConfigureAwait(false);
    }
}
