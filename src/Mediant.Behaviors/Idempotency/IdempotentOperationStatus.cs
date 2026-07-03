namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Outcome of <see cref="IIdempotentOperationCoordinator.BeginAsync{TResponse}"/>.
/// </summary>
public enum IdempotentOperationStatus
{
    /// <summary>
    /// No stored result exists for the key — the caller owns the operation and must either
    /// execute it and call <see cref="IdempotentOperation{TResponse}.CompleteAsync"/> or dispose
    /// without completing (leaving no trace, so a later retry can run).
    /// </summary>
    New = 0,

    /// <summary>
    /// A result is already stored for the key (and the fingerprint matched, when one was
    /// supplied) — serve <see cref="IdempotentOperation{TResponse}.StoredResponse"/> instead of
    /// executing again.
    /// </summary>
    Replay = 1,

    /// <summary>
    /// Another caller currently holds the per-key lock and the requested wait timeout elapsed.
    /// Only returned when a finite lock-wait timeout was passed to
    /// <see cref="IIdempotentOperationCoordinator.BeginAsync{TResponse}"/> (HTTP semantics: 409).
    /// </summary>
    InFlight = 2,

    /// <summary>
    /// A result is stored for the key but it was produced from a payload with a different
    /// fingerprint — the key is being reused for a different request (HTTP semantics: 422).
    /// </summary>
    FingerprintMismatch = 3,
}
