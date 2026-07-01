using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that prevents duplicate command execution using idempotency keys.
/// Queries are automatically skipped. Concurrent requests with the same key are serialized
/// via per-key locking to prevent duplicate handler execution.
/// </summary>
public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 600;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly IdempotentAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(IdempotentAttribute), true)
            .Cast<IdempotentAttribute>()
            .FirstOrDefault();

    private readonly IIdempotencyStore? _store;
    private readonly ILogger<IdempotencyBehavior<TRequest, TResponse>> _logger;
    private readonly IdempotencyBehaviorOptions _options;

    // Cached type check — runs once per closed generic type, not per request
    private static readonly bool IsQueryType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

    // Per-key lock pool shared across all IdempotencyBehavior instances
    private static readonly BoundedLockPool KeyLocks = new();

    public IdempotencyBehavior(
        ILogger<IdempotencyBehavior<TRequest, TResponse>> logger,
        IOptions<IdempotencyBehaviorOptions> options,
        IIdempotencyStore? store = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _store = store;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        // Skip queries
        if (IsQueryType)
        {
            return await next().ConfigureAwait(false);
        }

        if (CachedAttribute is not { } idempotentAttr)
        {
            return await next().ConfigureAwait(false);
        }

        // Window 0 means no check
        if (idempotentAttr.WindowSeconds <= 0)
        {
            return await next().ConfigureAwait(false);
        }

        // Store down — execute normally
        if (_store is null)
        {
            _logger.LogWarning("IIdempotencyStore not configured, executing request without idempotency check");
            return await next().ConfigureAwait(false);
        }

        var idempotencyKey = GenerateKey(request, idempotentAttr.KeyProperty);
        var window = TimeSpan.FromSeconds(idempotentAttr.WindowSeconds);

        // Per-key lock: concurrent requests with the same idempotency key are serialized.
        // The reference-counted Releaser keeps the lock alive until disposed.
        using var keyLock = await KeyLocks.AcquireAsync(idempotencyKey, cancellationToken).ConfigureAwait(false);

        // Check if already processed (inside lock to prevent race conditions).
        if (await _store.ExistsAsync(idempotencyKey, cancellationToken).ConfigureAwait(false))
        {
            var cached = await _store.GetAsync<TResponse>(idempotencyKey, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                _logger.LogInformation("Idempotent request {RequestName} with key {Key} returned cached result",
                    typeof(TRequest).Name, idempotencyKey);
                return cached;
            }
        }

        // Execute the request. The result is stored ONLY after the handler succeeds, so a handler
        // failure leaves no entry to clean up — and crucially must never delete a previously
        // stored successful result (the prior bug removed the key on any failure, including
        // failures on the cache-read path above).
        var response = await next().ConfigureAwait(false);

        await _store.SetAsync(idempotencyKey, response, window, cancellationToken).ConfigureAwait(false);

        return response;
    }

    // Pinned options so idempotency keys are deterministic and independent of any app-wide
    // JsonSerializerOptions configuration.
    private static readonly JsonSerializerOptions KeySerializerOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
    };

    private static string GenerateKey(TRequest request, string? keyPropertyName)
    {
        var typeName = typeof(TRequest).FullName ?? typeof(TRequest).Name;

        string material;
        if (!string.IsNullOrEmpty(keyPropertyName))
        {
            // Use only the designated key property so incidental fields (timestamps, correlation
            // ids) don't defeat deduplication of an otherwise-identical retried request.
            var prop = typeof(TRequest).GetProperty(keyPropertyName);
            if (prop is null)
            {
                throw new InvalidOperationException(
                    $"[Idempotent] KeyProperty '{keyPropertyName}' was not found on request type '{typeName}'.");
            }

            var value = prop.GetValue(request);
            material = value?.ToString() ?? string.Empty;
        }
        else
        {
            material = JsonSerializer.Serialize(request, KeySerializerOptions);
        }

        var combined = $"{typeName}:{material}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        return Convert.ToHexString(hash);
    }
}
