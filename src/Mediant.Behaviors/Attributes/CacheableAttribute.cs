namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a query for response caching. Commands are automatically skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class CacheableAttribute : Attribute
{
    /// <summary>
    /// Gets the cache duration in seconds.
    /// </summary>
    public int DurationSeconds { get; }

    /// <summary>
    /// Gets or sets a custom cache key prefix. If null, the request type name is used.
    /// </summary>
    public string? CacheKeyPrefix { get; set; }

    /// <summary>
    /// Initializes a new instance of <see cref="CacheableAttribute"/>.
    /// </summary>
    /// <param name="durationSeconds">Cache duration in seconds. 0 disables caching.</param>
    public CacheableAttribute(int durationSeconds = 300)
    {
        DurationSeconds = durationSeconds;
    }
}
