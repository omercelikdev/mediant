namespace Mediant.Exceptions;

/// <summary>
/// Thrown when multiple handlers are registered for a request type that requires exactly one handler.
/// </summary>
public sealed class MultipleHandlersException : InvalidOperationException
{
    /// <summary>
    /// Gets the request type that had multiple handlers.
    /// </summary>
    public Type RequestType { get; }

    /// <summary>
    /// Gets the number of handlers found.
    /// </summary>
    public int HandlerCount { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MultipleHandlersException"/>.
    /// </summary>
    /// <param name="requestType">The request type that had multiple handlers.</param>
    /// <param name="handlerCount">The number of handlers found.</param>
    public MultipleHandlersException(Type requestType, int handlerCount)
        : base($"Multiple handlers ({handlerCount}) registered for request type '{requestType.FullName}'. " +
               $"Each request must have exactly one handler. Remove duplicate registrations.")
    {
        RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
        HandlerCount = handlerCount;
    }
}
