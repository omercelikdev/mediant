using Mediant.Abstractions;

namespace Mediant.Implementation;

/// <summary>
/// Sequential notification publisher — executes handlers one by one in order.
/// If one handler throws, behavior depends on configuration:
/// continue (log and proceed) or stop (throw immediately).
/// </summary>
public sealed class ForeachNotificationPublisher : INotificationPublisher
{
    private readonly bool _stopOnFirstError;

    /// <summary>
    /// Initializes a new instance of <see cref="ForeachNotificationPublisher"/>.
    /// </summary>
    /// <param name="stopOnFirstError">Whether to stop on the first handler error. Default is true.</param>
    public ForeachNotificationPublisher(bool stopOnFirstError = true)
    {
        _stopOnFirstError = stopOnFirstError;
    }

    /// <inheritdoc />
    public async ValueTask Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlerExecutors,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlerExecutors);
        ArgumentNullException.ThrowIfNull(notification);

        if (handlerExecutors.Count == 0)
        {
            return;
        }

        List<Exception>? exceptions = null;

        for (int i = 0; i < handlerExecutors.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handlerExecutors[i].HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!_stopOnFirstError)
            {
                exceptions ??= new List<Exception>();
                exceptions.Add(ex);
            }
        }

        if (exceptions is { Count: > 0 })
        {
            throw new AggregateException(
                "One or more notification handlers threw an exception.",
                exceptions);
        }
    }
}
