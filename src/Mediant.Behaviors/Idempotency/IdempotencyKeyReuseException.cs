namespace Mediant.Behaviors.Idempotency;

/// <summary>
/// Thrown when an idempotency key is reused with a different request payload while
/// <c>[Idempotent(DetectPayloadMismatch = true)]</c> is in effect. This is a client error
/// (HTTP semantics: 422) — the caller sent a key that identifies a previously completed
/// operation together with a payload that does not match it.
/// </summary>
public sealed class IdempotencyKeyReuseException : InvalidOperationException
{
    /// <summary>Initializes a new instance of <see cref="IdempotencyKeyReuseException"/>.</summary>
    public IdempotencyKeyReuseException()
        : base("The idempotency key was already used with a different request payload.")
    {
    }

    /// <summary>Initializes a new instance of <see cref="IdempotencyKeyReuseException"/>.</summary>
    /// <param name="message">The exception message.</param>
    public IdempotencyKeyReuseException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="IdempotencyKeyReuseException"/>.</summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public IdempotencyKeyReuseException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The request type whose key was reused, when known.</summary>
    public string? RequestType { get; init; }
}
