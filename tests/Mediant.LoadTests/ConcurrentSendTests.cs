using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.LoadTests;

// Test types for load tests
public sealed record LoadTestCommand(string Data) : ICommand<Result>;
public sealed class LoadTestCommandHandler : ICommandHandler<LoadTestCommand>
{
    public ValueTask<Result> Handle(LoadTestCommand request, CancellationToken ct)
        => new(Result.Success());
}

public sealed record LoadTestQuery(int Id) : IQuery<Result<string>>;
public sealed class LoadTestQueryHandler : IQueryHandler<LoadTestQuery, Result<string>>
{
    public ValueTask<Result<string>> Handle(LoadTestQuery request, CancellationToken ct)
        => new(Result<string>.Success($"Item-{request.Id}"));
}

public sealed record LoadTestNotification(string Message) : INotification;
public sealed class LoadTestNotificationHandler1 : INotificationHandler<LoadTestNotification>
{
    public ValueTask Handle(LoadTestNotification n, CancellationToken ct) => ValueTask.CompletedTask;
}
public sealed class LoadTestNotificationHandler2 : INotificationHandler<LoadTestNotification>
{
    public ValueTask Handle(LoadTestNotification n, CancellationToken ct) => ValueTask.CompletedTask;
}
public sealed class LoadTestNotificationHandler3 : INotificationHandler<LoadTestNotification>
{
    public ValueTask Handle(LoadTestNotification n, CancellationToken ct) => ValueTask.CompletedTask;
}

public class ConcurrentSendTests
{
    private IMediator CreateMediator()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ConcurrentSendTests).Assembly));
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Should_Handle_10000_Concurrent_Requests_Without_Deadlock()
    {
        var mediator = CreateMediator();
        var tasks = new Task<Result>[10_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Send(new LoadTestCommand($"data-{i}")).AsTask();
        }

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task Should_Handle_100000_Sequential_Requests_Without_Memory_Leak()
    {
        var mediator = CreateMediator();

        // Warm up
        for (int i = 0; i < 100; i++)
        {
            await mediator.Send(new LoadTestCommand($"warmup-{i}"));
        }

        // A leak canary, not a precise benchmark: parallel test classes in this process and GC
        // timing on shared CI runners add noise to absolute measurements (observed 16MB once on a
        // GitHub runner with no leak present). A real per-request leak grows on EVERY 100k batch,
        // so re-measuring makes the check deterministic: only persistent growth fails all attempts.
        const double ThresholdMb = 16;
        double memGrowthMb = double.MaxValue;

        for (var attempt = 0; attempt < 3 && memGrowthMb >= ThresholdMb; attempt++)
        {
            var memBefore = MeasureStableMemory();

            for (int i = 0; i < 100_000; i++)
            {
                var result = await mediator.Send(new LoadTestCommand($"data-{i}"));
                result.IsSuccess.Should().BeTrue();
            }

            var memAfter = MeasureStableMemory();
            memGrowthMb = (memAfter - memBefore) / (1024.0 * 1024.0);
        }

        memGrowthMb.Should().BeLessThan(ThresholdMb, "memory should not grow persistently for cached pipelines");
    }

    private static long MeasureStableMemory()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(forceFullCollection: true);
    }

    [Fact]
    public async Task Should_Handle_Concurrent_Queries()
    {
        var mediator = CreateMediator();
        var tasks = new Task<Result<string>>[5_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            var id = i;
            tasks[i] = mediator.Send(new LoadTestQuery(id)).AsTask();
        }

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
        {
            r.IsSuccess.Should().BeTrue();
            r.Value.Should().StartWith("Item-");
        });
    }

    [Fact]
    public async Task Should_Handle_Notification_Fanout_1000x3()
    {
        var mediator = CreateMediator();
        var tasks = new Task[1_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Publish(new LoadTestNotification($"msg-{i}")).AsTask();
        }

        await Task.WhenAll(tasks); // 1000 notifications x 3 handlers = 3000 handler executions
    }

    [Fact]
    public async Task Should_Handle_Mixed_Concurrent_Operations()
    {
        var mediator = CreateMediator();
        var tasks = new List<Task>();

        for (int i = 0; i < 1_000; i++)
        {
            tasks.Add(mediator.Send(new LoadTestCommand($"cmd-{i}")).AsTask());
            tasks.Add(mediator.Send(new LoadTestQuery(i)).AsTask());
            tasks.Add(mediator.Publish(new LoadTestNotification($"notif-{i}")).AsTask());
        }

        await Task.WhenAll(tasks); // 3000 mixed concurrent operations
    }

    [Fact]
    public async Task Should_Handle_Sustained_Load_5_Seconds()
    {
        var mediator = CreateMediator();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var count = 0L;
        var errors = 0L;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await mediator.Send(new LoadTestCommand($"sustained-{count}"));
                if (result.IsSuccess)
                    Interlocked.Increment(ref count);
                else
                    Interlocked.Increment(ref errors);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        count.Should().BeGreaterThan(1000, "should handle >1000 req in 5 seconds");
        errors.Should().Be(0);
    }
}
