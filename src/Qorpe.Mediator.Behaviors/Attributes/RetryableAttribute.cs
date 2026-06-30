namespace Qorpe.Mediator.Behaviors.Attributes;

/// <summary>
/// Marks a request for automatic retry on transient failures.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RetryableAttribute : Attribute
{
    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    public int MaxRetryCount { get; }

    private int _initialDelayMs = 200;

    /// <summary>
    /// Gets or sets the initial delay in milliseconds before the first retry. Default is 200.
    /// Non-positive values are coerced to the default to keep backoff math well-defined.
    /// </summary>
    public int InitialDelayMs
    {
        get => _initialDelayMs;
        set => _initialDelayMs = value > 0 ? value : 200;
    }

    /// <summary>
    /// Gets or sets whether to use exponential backoff. Default is true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of <see cref="RetryableAttribute"/>.
    /// </summary>
    /// <param name="maxRetryCount">Maximum retry attempts. 0 means pass through without retry.</param>
    public RetryableAttribute(int maxRetryCount = 3)
    {
        MaxRetryCount = maxRetryCount;
    }
}
