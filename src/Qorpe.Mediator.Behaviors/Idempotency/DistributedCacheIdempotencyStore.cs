using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Configuration;

namespace Qorpe.Mediator.Behaviors.Idempotency;

/// <summary>
/// Production <see cref="IIdempotencyStore"/> backed by <see cref="IDistributedCache"/>. Works with
/// any distributed-cache provider the app configures (Redis, SQL Server, etc.) without coupling to a
/// specific one. Responses are stored as JSON; <c>Result</c>/<c>Result&lt;T&gt;</c> round-trip via
/// their built-in converters.
/// </summary>
public sealed class DistributedCacheIdempotencyStore : IIdempotencyStore
{
    private static readonly JsonSerializerOptions DefaultSerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private readonly IDistributedCache _cache;
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="DistributedCacheIdempotencyStore"/>.
    /// </summary>
    public DistributedCacheIdempotencyStore(IDistributedCache cache, IOptions<IdempotencyBehaviorOptions>? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _serializerOptions = options?.Value.SerializerOptions ?? DefaultSerializerOptions;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        var bytes = await _cache.GetAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        return bytes is { Length: > 0 };
    }

    /// <inheritdoc />
    public async ValueTask<TResponse?> GetAsync<TResponse>(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        var bytes = await _cache.GetAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(bytes, _serializerOptions);
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<TResponse>(string idempotencyKey, TResponse response, TimeSpan window, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, _serializerOptions);
        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = window };
        await _cache.SetAsync(idempotencyKey, bytes, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(idempotencyKey);
        return new ValueTask(_cache.RemoveAsync(idempotencyKey, cancellationToken));
    }
}
