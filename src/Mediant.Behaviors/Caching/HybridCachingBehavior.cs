using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Caching;

/// <summary>
/// Query caching backed by <see cref="HybridCache"/> (in-process L1 + distributed L2, with built-in
/// stampede protection and tag-based invalidation). An alternative to the
/// <see cref="Mediant.Behaviors.Behaviors.CachingBehavior{TRequest, TResponse}"/> distributed-cache
/// path; register via <c>AddMediantHybridCaching()</c>. Commands are skipped.
/// <para>
/// A <c>[Cacheable(CacheKeyPrefix = "p")]</c> query is tagged with <c>p</c>, so
/// <c>[InvalidatesCache("p")]</c> maps to an O(1) <c>HybridCache.RemoveByTagAsync</c> — no
/// key registry needed.
/// </para>
/// </summary>
public sealed class HybridCachingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 1000;

    private static readonly CacheableAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(CacheableAttribute), true)
            .Cast<CacheableAttribute>()
            .FirstOrDefault();

    private static readonly bool IsCommandType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static readonly JsonSerializerOptions KeySerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private readonly HybridCache _cache;
    private readonly ILogger<HybridCachingBehavior<TRequest, TResponse>> _logger;
    private readonly CachingBehaviorOptions _options;

    public HybridCachingBehavior(
        HybridCache cache,
        ILogger<HybridCachingBehavior<TRequest, TResponse>> logger,
        IOptions<CachingBehaviorOptions> options)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || IsCommandType || CachedAttribute is not { } cacheableAttr || cacheableAttr.DurationSeconds <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        var cacheKey = GenerateCacheKey(request, cacheableAttr.CacheKeyPrefix);
        var entryOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromSeconds(cacheableAttr.DurationSeconds),
        };

        // Every entry carries the all-entries tag (so InvalidateAllAsync is one RemoveByTag) plus
        // its prefix tag when present (so [InvalidatesCache(prefix)] is O(1)). HybridCache runs the
        // factory only on a miss and provides stampede protection internally.
        var tags = cacheableAttr.CacheKeyPrefix is { } prefix
            ? new[] { HybridCacheInvalidator.AllEntriesTag, prefix }
            : new[] { HybridCacheInvalidator.AllEntriesTag };

        _logger.LogDebug("HybridCache lookup for {RequestName} with key {CacheKey}", typeof(TRequest).Name, cacheKey);

        return await _cache.GetOrCreateAsync(
            cacheKey,
            (Next: next, cancellationToken),
            static async (state, _) => await state.Next().ConfigureAwait(false),
            entryOptions,
            tags,
            cancellationToken).ConfigureAwait(false);
    }

    private static string GenerateCacheKey(TRequest request, string? prefix)
    {
        var typeName = prefix ?? typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var json = JsonSerializer.Serialize(request, KeySerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"{typeName}:{Convert.ToHexString(hash)}";
    }
}
