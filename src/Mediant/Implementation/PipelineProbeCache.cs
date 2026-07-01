using System.Collections.Concurrent;

namespace Mediant.Implementation;

/// <summary>
/// Per-container cache that tracks whether each request type has pipeline elements
/// (pre/post processors, behaviors). Registered as Singleton — each root container
/// gets its own instance, ensuring independent behavior resolution across containers.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
internal sealed class PipelineProbeCache
{
    private readonly ConcurrentDictionary<Type, bool> _handlerOnlyFlags = new();

    /// <summary>
    /// Checks if the pipeline state for a request type has been probed and cached.
    /// </summary>
    /// <param name="requestType">The concrete request type.</param>
    /// <param name="isHandlerOnly">True if the request type has no processors or behaviors.</param>
    /// <returns>True if the result was found in cache.</returns>
    public bool TryGetHandlerOnly(Type requestType, out bool isHandlerOnly)
        => _handlerOnlyFlags.TryGetValue(requestType, out isHandlerOnly);

    /// <summary>
    /// Caches whether a request type is handler-only (no processors or behaviors).
    /// Uses TryAdd — first write wins, subsequent writes for the same type are ignored.
    /// </summary>
    public void SetHandlerOnly(Type requestType, bool isHandlerOnly)
        => _handlerOnlyFlags.TryAdd(requestType, isHandlerOnly);

    /// <summary>
    /// Clears the cache. For testing purposes only.
    /// </summary>
    internal void Clear() => _handlerOnlyFlags.Clear();
}
