namespace Mediant.Abstractions;

/// <summary>
/// Provides programmatic cache invalidation for cached query results.
/// Use after commands that modify data to ensure query cache consistency.
/// </summary>
public interface ICacheInvalidator
{
    /// <summary>
    /// Invalidates all cache entries whose keys start with the given prefix.
    /// </summary>
    /// <param name="keyPrefix">The cache key prefix to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask InvalidateAsync(string keyPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates all cached entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask InvalidateAllAsync(CancellationToken cancellationToken = default);
}
