namespace Mediant.Abstractions;

/// <summary>
/// Defines the mediator interface combining sender and publisher capabilities.
/// </summary>
public interface IMediator : ISender, IPublisher
{
}

/// <summary>
/// Defines the sender interface for dispatching requests to a single handler.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a request to a single handler.
    /// </summary>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="Exceptions.HandlerNotFoundException">Thrown when no handler is registered.</exception>
    /// <exception cref="Exceptions.MultipleHandlersException">Thrown when multiple handlers are registered.</exception>
    ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a stream of responses from a streaming request handler.
    /// </summary>
    /// <typeparam name="TResponse">The type of each item in the stream.</typeparam>
    /// <param name="request">The streaming request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of response items.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    /// <exception cref="Exceptions.HandlerNotFoundException">Thrown when no handler is registered.</exception>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the publisher interface for dispatching notifications to multiple handlers.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <typeparam name="TNotification">The notification type.</typeparam>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is null.</exception>
    ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification;

    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification"/> is null.</exception>
    ValueTask Publish(INotification notification, CancellationToken cancellationToken = default);
}
