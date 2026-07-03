using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;

namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Default <see cref="IIdempotentOperationCoordinator"/> backed by a bounded per-key lock pool and
/// the registered <see cref="IIdempotencyStore"/>. Register as a singleton so all entry points in
/// the process serialize on the same locks.
/// </summary>
public sealed class DefaultIdempotentOperationCoordinator : IIdempotentOperationCoordinator
{
    private readonly IIdempotencyStore? _store;
    private readonly BoundedLockPool _keyLocks = new();

    /// <summary>Initializes a new instance of <see cref="DefaultIdempotentOperationCoordinator"/>.</summary>
    /// <param name="store">The idempotency store; required at first use.</param>
    public DefaultIdempotentOperationCoordinator(IIdempotencyStore? store = null)
    {
        _store = store;
    }

    /// <inheritdoc />
    public async ValueTask<IdempotentOperation<TResponse>> BeginAsync<TResponse>(
        string key,
        string? fingerprint = null,
        TimeSpan? lockWaitTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (_store is null)
        {
            throw new InvalidOperationException(
                "No IIdempotencyStore is registered. Register one (e.g. AddMediantDistributedCacheIdempotencyStore()) before using the coordinator.");
        }

        BoundedLockPool.Releaser releaser;
        if (lockWaitTimeout is null)
        {
            releaser = await _keyLocks.AcquireAsync(key, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var acquired = await _keyLocks.TryAcquireAsync(key, lockWaitTimeout.Value, cancellationToken).ConfigureAwait(false);
            if (acquired is null)
            {
                return new IdempotentOperation<TResponse>(
                    IdempotentOperationStatus.InFlight, default, _store, key, fingerprint, lockReleaser: null);
            }

            releaser = acquired.Value;
        }

        try
        {
            var entry = await _store.GetAsync<IdempotencyEntry<TResponse>>(key, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                var status = fingerprint is not null && entry.Fingerprint is not null && !string.Equals(fingerprint, entry.Fingerprint, StringComparison.Ordinal)
                    ? IdempotentOperationStatus.FingerprintMismatch
                    : IdempotentOperationStatus.Replay;

                return new IdempotentOperation<TResponse>(status, entry.Response, _store, key, fingerprint, releaser);
            }

            return new IdempotentOperation<TResponse>(
                IdempotentOperationStatus.New, default, _store, key, fingerprint, releaser);
        }
        catch
        {
            releaser.Dispose();
            throw;
        }
    }
}
