using Mediant.Abstractions;

namespace Mediant.Implementation;

/// <summary>
/// Parallel notification publisher — executes all handlers concurrently.
/// Collects all exceptions and throws <see cref="AggregateException"/> if any fail.
/// </summary>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    private readonly TimeSpan? _timeout;

    /// <summary>
    /// Initializes a new instance of <see cref="ParallelNotificationPublisher"/>.
    /// </summary>
    /// <param name="timeout">Optional timeout for all handlers to complete.</param>
    public ParallelNotificationPublisher(TimeSpan? timeout = null)
    {
        _timeout = timeout;
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

        if (handlerExecutors.Count == 1)
        {
            // Optimize single handler — no need for Task.WhenAll
            await handlerExecutors[0].HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tasks = new Task[handlerExecutors.Count];
        for (int i = 0; i < handlerExecutors.Count; i++)
        {
            try
            {
                tasks[i] = handlerExecutors[i].HandlerCallback(notification, cancellationToken).AsTask();
            }
            catch (Exception ex)
            {
                // Handler threw synchronously — wrap in faulted task
                tasks[i] = Task.FromException(ex);
            }
        }

        if (_timeout.HasValue)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout.Value);

            try
            {
                await Task.WhenAll(tasks).WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred — collect exceptions from handlers that failed before the timeout
                CollectAndThrowWithTimeout(tasks, _timeout.Value);
            }
            catch (Exception) when (HasPendingTasks(tasks))
            {
                // Some handlers failed but others are still running — wait for timeout then report all
                try
                {
                    await Task.WhenAll(tasks).WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    CollectAndThrowWithTimeout(tasks, _timeout.Value);
                }
                catch
                {
                    // All tasks completed (with failures) — let the normal WhenAll path handle it
                }

                // Re-run WhenAll to propagate all exceptions via AggregateException
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        else
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private static bool HasPendingTasks(Task[] tasks)
    {
        for (int i = 0; i < tasks.Length; i++)
        {
            if (!tasks[i].IsCompleted)
                return true;
        }
        return false;
    }

    private static void CollectAndThrowWithTimeout(Task[] tasks, TimeSpan timeout)
    {
        var handlerExceptions = new List<Exception>();
        for (int i = 0; i < tasks.Length; i++)
        {
            if (tasks[i].IsFaulted && tasks[i].Exception is { } taskEx)
            {
                handlerExceptions.AddRange(taskEx.InnerExceptions);
            }
        }

        var timeoutException = new TimeoutException(
            $"Notification handlers did not complete within the timeout of {timeout.TotalMilliseconds}ms. " +
            $"{handlerExceptions.Count} handler(s) also failed with exceptions.");

        if (handlerExceptions.Count > 0)
        {
            throw new AggregateException(
                timeoutException.Message,
                new Exception[] { timeoutException }.Concat(handlerExceptions));
        }

        throw timeoutException;
    }
}
