namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a command as invalidating cached entries with the specified key prefix
/// after successful execution.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class InvalidatesCacheAttribute : Attribute
{
    /// <summary>
    /// Gets the cache key prefix to invalidate.
    /// </summary>
    public string KeyPrefix { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="InvalidatesCacheAttribute"/>.
    /// </summary>
    /// <param name="keyPrefix">The cache key prefix to invalidate after successful execution.</param>
    public InvalidatesCacheAttribute(string keyPrefix)
    {
        KeyPrefix = keyPrefix ?? throw new ArgumentNullException(nameof(keyPrefix));
    }
}
