using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.LoadTests;

// === Types for pipeline load tests ===
public sealed record PipelineCommand(string Data) : ICommand<Result>;
public sealed class PipelineCommandHandler : ICommandHandler<PipelineCommand>
{
    public ValueTask<Result> Handle(PipelineCommand request, CancellationToken cancellationToken)
        => new(Result.Success());
}

// Re-entrant: handler calls Send() internally
public sealed record OuterCommand(int Depth) : ICommand<Result>;
public sealed class OuterCommandHandler : ICommandHandler<OuterCommand>
{
    private readonly ISender _sender;
    public OuterCommandHandler(ISender sender) => _sender = sender;

    public async ValueTask<Result> Handle(OuterCommand request, CancellationToken cancellationToken)
    {
        if (request.Depth > 0)
        {
            await _sender.Send(new OuterCommand(request.Depth - 1), cancellationToken);
        }
        return Result.Success();
    }
}

// Failing handler for exception tests
public sealed record FailingCommand(bool ShouldFail) : ICommand<Result>;
public sealed class FailingCommandHandler : ICommandHandler<FailingCommand>
{
    public ValueTask<Result> Handle(FailingCommand request, CancellationToken cancellationToken)
    {
        if (request.ShouldFail)
            throw new InvalidOperationException("Intentional failure");
        return new(Result.Success());
    }
}

// Stream type
public sealed record LoadStreamRequest(int Count) : IStreamRequest<int>;
public sealed class LoadStreamHandler : IStreamRequestHandler<LoadStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(LoadStreamRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 0; i < request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }
    }
}

// Cacheable query for lock pool memory tests
[Cacheable(60)]
public sealed record CacheableLoadQuery(int Id) : IQuery<Result<string>>;
public sealed class CacheableLoadQueryHandler : IQueryHandler<CacheableLoadQuery, Result<string>>
{
    public ValueTask<Result<string>> Handle(CacheableLoadQuery request, CancellationToken cancellationToken)
        => new(Result<string>.Success($"cached-{request.Id}"));
}

public class PipelineUnderLoadTests
{
    private ServiceProvider BuildFullPipelineServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PipelineUnderLoadTests).Assembly));
        services.AddMediantLogging();
        services.AddMediantPerformanceMonitoring();
        services.AddMediantUnhandledExceptions();
        return services.BuildServiceProvider();
    }

    // === 1. BEHAVIOR PIPELINE UNDER CONCURRENT LOAD ===
    [Fact]
    public async Task Pipeline_With_3_Behaviors_50K_Concurrent_NoDeadlock()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = new Task<Result>[50_000];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Send(new PipelineCommand($"data-{i}")).AsTask();
        }

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    // === 2. SCOPED DI — REAL-WORLD PATTERN ===
    [Fact]
    public async Task Scoped_DI_10K_Concurrent_EachInOwnScope()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var tasks = new Task<Result>[10_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                // Each request in its own scope — like ASP.NET Core per-request
                using var scope = sp.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                return await mediator.Send(new PipelineCommand($"scoped-{idx}"));
            });
        }

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    // === 3. RE-ENTRANCY — HANDLER CALLS SEND() INSIDE ===
    [Fact]
    public async Task Reentrant_Send_1000_Concurrent_Depth3_NoDeadlock()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = new Task<Result>[1_000];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Send(new OuterCommand(3)).AsTask(); // Each goes 3 levels deep
        }

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    // === 4. EXCEPTION HANDLING UNDER LOAD ===
    [Fact]
    public async Task Exceptions_Under_Load_10K_Mixed_Success_Failure()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var successCount = 0L;
        var failCount = 0L;
        var tasks = new Task[10_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            var shouldFail = i % 3 == 0; // ~33% failures
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await mediator.Send(new FailingCommand(shouldFail));
                    Interlocked.Increment(ref successCount);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref failCount);
                }
            });
        }

        await Task.WhenAll(tasks);

        successCount.Should().BeGreaterThan(5000, "majority should succeed");
        failCount.Should().BeGreaterThan(2000, "~33% should fail");
        (successCount + failCount).Should().Be(10_000, "all requests must complete");
    }

    // === 5. CANCELLATION MID-FLIGHT ===
    [Fact]
    public async Task Cancellation_MidFlight_NoHangingTasks()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        var completed = 0L;
        var cancelled = 0L;
        var tasks = new Task[5_000];

        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    await mediator.Send(new PipelineCommand("cancel-test"), cts.Token);
                    Interlocked.Increment(ref completed);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref cancelled);
                }
            });
        }

        // Cancel after a short delay
        await Task.Delay(1);
        cts.Cancel();

        await Task.WhenAll(tasks);

        // All tasks must complete (either success or cancelled)
        (completed + cancelled).Should().Be(5_000, "no tasks should hang");
    }

    // === 6. STREAMING UNDER CONCURRENT LOAD ===
    [Fact]
    public async Task Streaming_100_Concurrent_Consumers_Each1000Items()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = new Task<int>[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                var count = 0;
                await foreach (var item in mediator.CreateStream(new LoadStreamRequest(1_000)))
                {
                    count++;
                }
                return count;
            });
        }

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(count => count.Should().Be(1_000));
    }

    // === 7. THREAD POOL — NO SYNC-OVER-ASYNC ===
    [Fact]
    public async Task ThreadPool_Not_Exhausted_Under_Heavy_Load()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        ThreadPool.GetMinThreads(out var minWorker, out var minIO);
        ThreadPool.GetAvailableThreads(out var availBefore, out _);

        var tasks = new Task[20_000];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Send(new PipelineCommand($"tp-{i}")).AsTask();
        }

        await Task.WhenAll(tasks);

        ThreadPool.GetAvailableThreads(out var availAfter, out _);

        // ThreadPool should recover — available threads shouldn't be permanently depleted
        // Allow some variance, but shouldn't lose more than 50% of threads
        availAfter.Should().BeGreaterThan(availBefore / 2,
            "thread pool should not be exhausted by async mediator operations");
    }

    // === 8. MEMORY STABILITY — LONG RUNNING WITH BEHAVIORS ===
    [Fact]
    public async Task Memory_Stable_500K_Sequential_With_Behaviors()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Warmup
        for (int i = 0; i < 1_000; i++)
        {
            await mediator.Send(new PipelineCommand($"warmup-{i}"));
        }

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        var memBefore = GC.GetTotalMemory(true);
        var gen0Before = GC.CollectionCount(0);

        for (int i = 0; i < 500_000; i++)
        {
            await mediator.Send(new PipelineCommand($"mem-{i}"));
        }

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        var memAfter = GC.GetTotalMemory(true);
        var gen0After = GC.CollectionCount(0);

        var memGrowthMb = (memAfter - memBefore) / (1024.0 * 1024.0);
        memGrowthMb.Should().BeLessThan(20,
            "memory should not grow significantly for 500K requests with cached pipelines");
    }

    // === 9. SUSTAINED THROUGHPUT WITH LATENCY ===
    [Fact]
    public async Task Sustained_10_Seconds_With_Latency_Percentiles()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var latencies = new List<double>();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var errors = 0L;

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                await mediator.Send(new PipelineCommand("sustained"));
                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        }

        latencies.Count.Should().BeGreaterThan(10_000, "should handle >10K req in 10 seconds");
        errors.Should().Be(0);

        latencies.Sort();
        var p50 = latencies[(int)(latencies.Count * 0.50)];
        var p95 = latencies[(int)(latencies.Count * 0.95)];
        var p99 = latencies[(int)(latencies.Count * 0.99)];

        p50.Should().BeLessThan(1.0, "p50 latency should be under 1ms");
        p95.Should().BeLessThan(5.0, "p95 latency should be under 5ms");
        p99.Should().BeLessThan(10.0, "p99 latency should be under 10ms");
    }

    // === 10. NOTIFICATION PARALLEL PUBLISHER UNDER LOAD ===
    [Fact]
    public async Task Parallel_Publish_5K_Notifications_10_Handlers()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddMediant(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PipelineUnderLoadTests).Assembly);
            cfg.NotificationPublishStrategy = NotificationPublishStrategy.Parallel;
        });
        // Add 7 more handlers (3 already registered from assembly scan)
        for (int i = 0; i < 7; i++)
        {
            services.AddTransient<INotificationHandler<LoadTestNotification>, LoadTestNotificationHandler1>();
        }

        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = new Task[5_000];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = mediator.Publish(new LoadTestNotification($"parallel-{i}")).AsTask();
        }

        // 5000 notifications x 10 handlers = 50,000 handler executions
        await Task.WhenAll(tasks);
    }

    // === 11. CACHING BEHAVIOR MEMORY STABILITY WITH HIGH-CARDINALITY KEYS ===
    [Fact]
    public async Task CachingBehavior_HighCardinality_Keys_Stable_Memory()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PipelineUnderLoadTests).Assembly));
        services.AddMediantCaching();
        services.AddDistributedMemoryCache();
        using var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            await mediator.Send(new CacheableLoadQuery(i));
        }

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        var memBefore = GC.GetTotalMemory(true);

        // Execute with 10K unique cache keys — previously this would leak semaphores
        for (int i = 0; i < 10_000; i++)
        {
            var result = await mediator.Send(new CacheableLoadQuery(i));
            result.IsSuccess.Should().BeTrue();
        }

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        var memAfter = GC.GetTotalMemory(true);

        var memGrowthMb = (memAfter - memBefore) / (1024.0 * 1024.0);
        memGrowthMb.Should().BeLessThan(20,
            "lock pool should not grow unbounded with high-cardinality cache keys");
    }

    // === 12. GRACEFUL DEGRADATION — MIXED LOAD WITH FAILURES ===
    [Fact]
    public async Task Graceful_Degradation_Mixed_Success_Fail_Cancel()
    {
        using var sp = BuildFullPipelineServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var success = 0L;
        var failed = 0L;
        var tasks = new Task[10_000];
        var rng = new Random(42);

        for (int i = 0; i < tasks.Length; i++)
        {
            var action = rng.Next(3);
            tasks[i] = action switch
            {
                0 => Task.Run(async () =>
                {
                    await mediator.Send(new PipelineCommand($"ok-{i}"));
                    Interlocked.Increment(ref success);
                }),
                1 => Task.Run(async () =>
                {
                    try
                    {
                        await mediator.Send(new FailingCommand(true));
                    }
                    catch (InvalidOperationException)
                    {
                        Interlocked.Increment(ref failed);
                    }
                }),
                _ => Task.Run(async () =>
                {
                    await mediator.Publish(new LoadTestNotification($"notif-{i}"));
                    Interlocked.Increment(ref success);
                })
            };
        }

        await Task.WhenAll(tasks);

        (success + failed).Should().Be(10_000, "all operations must complete");
        success.Should().BeGreaterThan(5_000);
        failed.Should().BeGreaterThan(2_000);
    }
}
