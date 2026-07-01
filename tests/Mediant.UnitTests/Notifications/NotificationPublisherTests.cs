using Mediant.Abstractions;
using Mediant.Implementation;

namespace Mediant.UnitTests.Notifications;

public class ForeachNotificationPublisherTests
{
    private static NotificationHandlerExecutor CreateExecutor(Func<INotification, CancellationToken, ValueTask> callback)
    {
        return new NotificationHandlerExecutor(new object(), callback);
    }

    [Fact]
    public async Task Publish_WithNoHandlers_ShouldSucceed()
    {
        var publisher = new ForeachNotificationPublisher();
        await publisher.Publish(Array.Empty<NotificationHandlerExecutor>(),
            Substitute.For<INotification>(), CancellationToken.None);
    }

    [Fact]
    public async Task Publish_Sequential_ShouldCallAllHandlers()
    {
        var publisher = new ForeachNotificationPublisher();
        var called = new List<int>();

        var executors = new[]
        {
            CreateExecutor((_, _) => { called.Add(1); return ValueTask.CompletedTask; }),
            CreateExecutor((_, _) => { called.Add(2); return ValueTask.CompletedTask; }),
            CreateExecutor((_, _) => { called.Add(3); return ValueTask.CompletedTask; })
        };

        await publisher.Publish(executors, Substitute.For<INotification>(), CancellationToken.None);
        called.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task Publish_StopOnFirstError_ShouldThrowImmediately()
    {
        var publisher = new ForeachNotificationPublisher(stopOnFirstError: true);
        var secondCalled = false;

        var executors = new[]
        {
            CreateExecutor((_, _) => throw new InvalidOperationException("fail")),
            CreateExecutor((_, _) => { secondCalled = true; return ValueTask.CompletedTask; })
        };

        var act = async () => await publisher.Publish(executors,
            Substitute.For<INotification>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        secondCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Publish_ContinueOnError_ShouldThrowAggregateException()
    {
        var publisher = new ForeachNotificationPublisher(stopOnFirstError: false);

        var executors = new[]
        {
            CreateExecutor((_, _) => throw new InvalidOperationException("fail1")),
            CreateExecutor((_, _) => throw new InvalidOperationException("fail2"))
        };

        var act = async () => await publisher.Publish(executors,
            Substitute.For<INotification>(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().HaveCount(2);
    }
}

public class ParallelNotificationPublisherTests
{
    private static NotificationHandlerExecutor CreateExecutor(Func<INotification, CancellationToken, ValueTask> callback)
    {
        return new NotificationHandlerExecutor(new object(), callback);
    }

    [Fact]
    public async Task Publish_WithNoHandlers_ShouldSucceed()
    {
        var publisher = new ParallelNotificationPublisher();
        await publisher.Publish(Array.Empty<NotificationHandlerExecutor>(),
            Substitute.For<INotification>(), CancellationToken.None);
    }

    [Fact]
    public async Task Publish_Parallel_ShouldCallAllHandlers()
    {
        var publisher = new ParallelNotificationPublisher();
        var count = 0;

        var executors = Enumerable.Range(0, 10).Select(_ =>
            CreateExecutor((_, _) => { Interlocked.Increment(ref count); return ValueTask.CompletedTask; })
        ).ToArray();

        await publisher.Publish(executors, Substitute.For<INotification>(), CancellationToken.None);
        count.Should().Be(10);
    }

    [Fact]
    public async Task Publish_WithTimeout_ShouldThrowTimeoutException()
    {
        var publisher = new ParallelNotificationPublisher(TimeSpan.FromMilliseconds(50));

        var executors = new[]
        {
            CreateExecutor(async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None)),
            CreateExecutor(async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
        };

        var act = async () => await publisher.Publish(executors,
            Substitute.For<INotification>(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Publish_WithTimeout_ShouldPreserveHandlerExceptions()
    {
        var publisher = new ParallelNotificationPublisher(TimeSpan.FromMilliseconds(100));

        var executors = new[]
        {
            // Handler that fails immediately
            CreateExecutor((_, _) => throw new InvalidOperationException("handler failed")),
            // Handler that runs forever (causes timeout)
            CreateExecutor(async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
        };

        var act = async () => await publisher.Publish(executors,
            Substitute.For<INotification>(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AggregateException>();
        ex.Which.InnerExceptions.Should().Contain(e => e is TimeoutException);
        ex.Which.InnerExceptions.Should().Contain(e => e is InvalidOperationException && e.Message == "handler failed");
    }

    [Fact]
    public async Task Publish_WithTimeout_NoHandlerFailures_ShouldThrowPlainTimeout()
    {
        var publisher = new ParallelNotificationPublisher(TimeSpan.FromMilliseconds(50));

        // Use 2 handlers to bypass single-handler optimization
        var executors = new[]
        {
            CreateExecutor(async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None)),
            CreateExecutor(async (_, ct) => await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None))
        };

        var act = async () => await publisher.Publish(executors,
            Substitute.For<INotification>(), CancellationToken.None);

        // No handler failures — should be plain TimeoutException, not AggregateException
        await act.Should().ThrowAsync<TimeoutException>();
    }
}
