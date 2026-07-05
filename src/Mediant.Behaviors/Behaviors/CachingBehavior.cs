using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Caching;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that caches query responses. Commands are automatically skipped.
/// Includes stampede prevention using a bounded lock pool with automatic eviction.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 1000;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly CacheableAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(CacheableAttribute), true)
            .Cast<CacheableAttribute>()
            .FirstOrDefault();

    private readonly IDistributedCache? _cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;
    private readonly ICacheKeyRegistry? _keyRegistry;

    // Cached type check — runs once per closed generic type, not per request
    private static readonly bool IsCommandType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    // Bounded lock pool for stampede prevention with automatic eviction. Created lazily so it
    // honors the configured MaxLockPoolSize; shared across all instances of this closed generic.
    private static BoundedLockPool? _lockPool;

    // Pinned options so cache keys are deterministic and independent of any app-wide
    // JsonSerializerOptions configuration.
    private static readonly JsonSerializerOptions KeySerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private BoundedLockPool GetLockPool()
    {
        var pool = Volatile.Read(ref _lockPool);
        if (pool is not null)
        {
            return pool;
        }

        var created = new BoundedLockPool(_options.MaxLockPoolSize);
        return Interlocked.CompareExchange(ref _lockPool, created, null) ?? created;
    }

    public CachingBehavior(
        ILogger<CachingBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions> options,
        IDistributedCache? cache = null,
        ICacheKeyRegistry? keyRegistry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _cache = cache;
        _keyRegistry = keyRegistry;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        // Skip commands — caching is for queries only
        if (IsCommandType)
        {
            return await next().ConfigureAwait(false);
        }

        if (CachedAttribute is not { } cacheableAttr)
        {
            return await next().ConfigureAwait(false);
        }

        // Duration 0 — no cache
        if (cacheableAttr.DurationSeconds <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        // Store down — fall through to handler
        if (_cache is null)
        {
            _logger.LogWarning("IDistributedCache not configured, executing request without caching");
            return await next().ConfigureAwait(false);
        }

        var cacheKey = GenerateCacheKey(request, cacheableAttr.CacheKeyPrefix);

        // Stampede prevention: acquire per-key lock from the bounded pool. The reference-counted
        // Releaser keeps the entry alive until disposed, so the lock can't be evicted mid-use.
        using var keyLock = await GetLockPool().AcquireAsync(cacheKey, cancellationToken).ConfigureAwait(false);

        {
            // Try to get from cache
            try
            {
                var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);
                if (cachedBytes is not null)
                {
                    var cached = JsonSerializer.Deserialize<TResponse>(cachedBytes, _options.SerializerOptions);
                    if (cached is not null)
                    {
                        _logger.LogDebug("Cache hit for {RequestName} with key {CacheKey}", typeof(TRequest).Name, cacheKey);
                        return cached;
                    }

                    // Type changed — cache miss, log it
                    _logger.LogWarning("Cache type mismatch for key {CacheKey}, treating as miss", cacheKey);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cache read failed for key {CacheKey}, falling through to handler", cacheKey);
            }

            // Execute handler
            var response = await next().ConfigureAwait(false);

            // Store in cache (null responses are valid)
            try
            {
                var ttl = TimeSpan.FromSeconds(cacheableAttr.DurationSeconds);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(response, _options.SerializerOptions);
                var cacheOptions = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                };
                await _cache.SetAsync(cacheKey, bytes, cacheOptions, cancellationToken).ConfigureAwait(false);

                // Record the key under its prefix so [InvalidatesCache("prefix")] can find it —
                // IDistributedCache cannot enumerate keys. Only prefixed cacheables participate in
                // prefix invalidation, so a key without an explicit prefix needs no registration.
                if (_keyRegistry is not null && cacheableAttr.CacheKeyPrefix is { } prefix)
                {
                    await _keyRegistry.RegisterAsync(prefix, cacheKey, ttl, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cache write failed for key {CacheKey}", cacheKey);
            }

            return response;
        }
    }

    private static string GenerateCacheKey(TRequest request, string? prefix)
    {
        var typeName = prefix ?? typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var json = JsonSerializer.Serialize(request, KeySerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"{typeName}:{Convert.ToHexString(hash)}";
    }

}
