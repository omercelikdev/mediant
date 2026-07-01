namespace Mediant.Abstractions;

/// <summary>
/// Defines the strategy for publishing notifications to handlers.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Publishes a notification to the given handlers.
    /// </summary>
    /// <param name="handlerExecutors">The notification handler executors.</param>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    ValueTask Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken);
}

/// <summary>
/// Wraps a notification handler for execution by the publisher.
/// </summary>
public sealed class NotificationHandlerExecutor
{
    /// <summary>
    /// Gets the handler instance.
    /// </summary>
    public object HandlerInstance { get; }

    /// <summary>
    /// Gets the handler callback.
    /// </summary>
    public Func<INotification, CancellationToken, ValueTask> HandlerCallback { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="NotificationHandlerExecutor"/>.
    /// </summary>
    /// <param name="handlerInstance">The handler instance.</param>
    /// <param name="handlerCallback">The handler callback.</param>
    public NotificationHandlerExecutor(object handlerInstance, Func<INotification, CancellationToken, ValueTask> handlerCallback)
    {
        HandlerInstance = handlerInstance ?? throw new ArgumentNullException(nameof(handlerInstance));
        HandlerCallback = handlerCallback ?? throw new ArgumentNullException(nameof(handlerCallback));
    }
}
