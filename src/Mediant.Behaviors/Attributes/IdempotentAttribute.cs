namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a command for idempotency checking. Queries are automatically skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class IdempotentAttribute : Attribute
{
    /// <summary>
    /// Gets the idempotency window in seconds. 0 means no check.
    /// </summary>
    public int WindowSeconds { get; }

    /// <summary>
    /// Gets or sets the name of the request property that supplies the idempotency key
    /// (for example a client-provided <c>IdempotencyKey</c>/<c>RequestId</c>). When set, only
    /// that property's value identifies the request, so retries carrying incidental differences
    /// (timestamps, correlation ids) still deduplicate. When null, the full serialized request
    /// is hashed.
    /// </summary>
    public string? KeyProperty { get; set; }

    /// <summary>
    /// Gets or sets whether key reuse with a different payload is detected. Only meaningful with
    /// <see cref="KeyProperty"/> (without it the key already hashes the full payload). When true,
    /// a SHA-256 fingerprint of the full request payload is stored with the response; a later
    /// request carrying the same key but a different payload throws
    /// <see cref="Mediant.Behaviors.Idempotency.IdempotencyKeyReuseException"/> instead of
    /// silently replaying the stored response (Stripe-style semantics). Off by default.
    /// </summary>
    public bool DetectPayloadMismatch { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="IdempotentAttribute"/>.
    /// </summary>
    /// <param name="windowSeconds">The idempotency window in seconds.</param>
    public IdempotentAttribute(int windowSeconds = 300)
    {
        WindowSeconds = windowSeconds;
    }
}
