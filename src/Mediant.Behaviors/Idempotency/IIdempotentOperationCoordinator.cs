namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Exposes the idempotency begin/complete lifecycle (per-key locking, stored-response replay,
/// optional payload-fingerprint verification) to non-mediator entry points such as an HTTP
/// <c>Idempotency-Key</c> middleware, backed by the same <see cref="Mediant.Abstractions.IIdempotencyStore"/>
/// the <c>[Idempotent]</c> pipeline behavior uses.
/// <para>
/// The per-key serialization guarantee is <b>process-local</b>: concurrent callers within one
/// process are serialized on the key, but replicas of a horizontally scaled service can still race
/// on the same key (last write wins at the store). Keys are used verbatim — namespace them
/// (e.g. <c>"http:orders:{key}"</c>) to avoid colliding with other producers on a shared store.
/// </para>
/// </summary>
public interface IIdempotentOperationCoordinator
{
    /// <summary>
    /// Begins an idempotent operation for <paramref name="key"/>: acquires the per-key lock and
    /// checks the store for a previously completed result. Inspect
    /// <see cref="IdempotentOperation{TResponse}.Status"/> on the returned handle, and always
    /// dispose it to release the lock.
    /// </summary>
    /// <typeparam name="TResponse">The response type stored for the operation.</typeparam>
    /// <param name="key">The idempotency key, used verbatim.</param>
    /// <param name="fingerprint">Optional payload fingerprint. When supplied and a stored entry
    /// carries a different fingerprint, the status is
    /// <see cref="IdempotentOperationStatus.FingerprintMismatch"/> instead of a silent replay.</param>
    /// <param name="lockWaitTimeout">How long to wait for the per-key lock. Null (default) waits
    /// indefinitely — the behavior's semantics. A finite value (e.g. <see cref="TimeSpan.Zero"/>)
    /// yields <see cref="IdempotentOperationStatus.InFlight"/> when the lock is not acquired in
    /// time, enabling 409-style HTTP responses.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<IdempotentOperation<TResponse>> BeginAsync<TResponse>(
        string key,
        string? fingerprint = null,
        TimeSpan? lockWaitTimeout = null,
        CancellationToken cancellationToken = default);
}
