namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Serialization envelope the <see cref="IIdempotentOperationCoordinator"/> persists in the
/// <see cref="Mediant.Abstractions.IIdempotencyStore"/>: the operation's response plus the optional
/// payload fingerprint used to detect key reuse with a different payload.
/// </summary>
/// <typeparam name="TResponse">The stored response type.</typeparam>
public sealed class IdempotencyEntry<TResponse>
{
    /// <summary>SHA-256 (or caller-defined) fingerprint of the request payload, or null when
    /// fingerprint checking is not used.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>The stored operation response.</summary>
    public TResponse? Response { get; init; }
}
