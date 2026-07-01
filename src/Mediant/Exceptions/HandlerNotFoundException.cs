namespace Mediant.Exceptions;

/// <summary>
/// Thrown when no handler is registered for a given request type.
/// </summary>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Gets the request type that had no handler.
    /// </summary>
    public Type RequestType { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HandlerNotFoundException"/>.
    /// </summary>
    /// <param name="requestType">The request type that had no handler.</param>
    public HandlerNotFoundException(Type requestType)
        : base($"No handler registered for request type '{requestType.FullName}'. " +
               $"Ensure a handler implementing IRequestHandler<{requestType.Name}, TResponse> is registered in the DI container. " +
               $"Verify that the assembly containing the handler is included in RegisterServicesFromAssembly().")
    {
        RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
    }

    /// <summary>
    /// Initializes a new instance with additional context about registered handlers.
    /// </summary>
    /// <param name="requestType">The request type that had no handler.</param>
    /// <param name="registeredHandlerCount">The total number of handlers registered in the container.</param>
    public HandlerNotFoundException(Type requestType, int registeredHandlerCount)
        : base($"No handler registered for request type '{requestType.FullName}'. " +
               $"The container has {registeredHandlerCount} handler(s) registered for other types. " +
               $"Ensure the assembly containing IRequestHandler<{requestType.Name}, TResponse> is included in RegisterServicesFromAssembly().")
    {
        RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HandlerNotFoundException"/>.
    /// </summary>
    /// <param name="requestType">The request type that had no handler.</param>
    /// <param name="innerException">The inner exception.</param>
    public HandlerNotFoundException(Type requestType, Exception innerException)
        : base($"No handler registered for request type '{requestType.FullName}'.", innerException)
    {
        RequestType = requestType ?? throw new ArgumentNullException(nameof(requestType));
    }
}
