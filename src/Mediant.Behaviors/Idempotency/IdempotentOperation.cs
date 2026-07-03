using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;

namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Handle for one idempotent operation begun via
/// <see cref="IIdempotentOperationCoordinator.BeginAsync{TResponse}"/>. Holds the per-key lock for
/// its lifetime (except <see cref="IdempotentOperationStatus.InFlight"/>) — always dispose it.
/// </summary>
/// <typeparam name="TResponse">The response type stored for the operation.</typeparam>
public sealed class IdempotentOperation<TResponse> : IDisposable
{
    private readonly IIdempotencyStore? _store;
    private readonly string _key;
    private readonly string? _fingerprint;
    private BoundedLockPool.Releaser? _lockReleaser;
    private bool _completed;

    internal IdempotentOperation(
        IdempotentOperationStatus status,
        TResponse? storedResponse,
        IIdempotencyStore? store,
        string key,
        string? fingerprint,
        BoundedLockPool.Releaser? lockReleaser)
    {
        Status = status;
        StoredResponse = storedResponse;
        _store = store;
        _key = key;
        _fingerprint = fingerprint;
        _lockReleaser = lockReleaser;
    }

    /// <summary>The outcome of beginning the operation.</summary>
    public IdempotentOperationStatus Status { get; }

    /// <summary>The previously stored response when <see cref="Status"/> is
    /// <see cref="IdempotentOperationStatus.Replay"/>; default otherwise.</summary>
    public TResponse? StoredResponse { get; }

    /// <summary>
    /// Persists the operation's response (with the fingerprint passed to
    /// <c>BeginAsync</c>, if any) so later calls with the same key replay it for
    /// <paramref name="window"/>. Only valid when <see cref="Status"/> is
    /// <see cref="IdempotentOperationStatus.New"/>, at most once, while the handle is undisposed.
    /// If the operation fails, dispose without completing — nothing is stored and a retry can run.
    /// </summary>
    /// <param name="response">The response to store.</param>
    /// <param name="window">How long the stored response replays.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask CompleteAsync(TResponse response, TimeSpan window, CancellationToken cancellationToken = default)
    {
        if (Status != IdempotentOperationStatus.New)
        {
            throw new InvalidOperationException(
                $"CompleteAsync is only valid for a {nameof(IdempotentOperationStatus.New)} operation (status: {Status}).");
        }

        if (_completed)
        {
            throw new InvalidOperationException("CompleteAsync was already called for this operation.");
        }

        ObjectDisposedException.ThrowIf(_lockReleaser is null, this);

        var entry = new IdempotencyEntry<TResponse> { Fingerprint = _fingerprint, Response = response };
        await _store!.SetAsync(_key, entry, window, cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Releases the per-key lock. Safe to call multiple times.</summary>
    public void Dispose()
    {
        _lockReleaser?.Dispose();
        _lockReleaser = null;
    }
}
