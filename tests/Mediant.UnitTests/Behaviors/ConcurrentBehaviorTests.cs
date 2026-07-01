using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class ConcurrentBehaviorTests
{
    [Fact]
    public async Task LoggingBehavior_Should_Handle_100_Concurrent_Requests()
    {
        var logger = Substitute.For<ILogger<LoggingBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new LoggingBehaviorOptions());
        var behavior = new LoggingBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var tasks = Enumerable.Range(0, 100).Select(i =>
            behavior.Handle(new TestCommand($"concurrent-{i}"), next, CancellationToken.None).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task RetryBehavior_Should_Handle_Concurrent_Requests_Without_Deadlock()
    {
        var logger = Substitute.For<ILogger<RetryBehavior<RetryableCommand, Result>>>();
        var opts = Options.Create(new RetryBehaviorOptions());
        var behavior = new RetryBehavior<RetryableCommand, Result>(logger, opts);

        // Each request succeeds on first try — test concurrent access to behavior internals
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var tasks = Enumerable.Range(0, 100).Select(_ =>
            behavior.Handle(new RetryableCommand("data"), next, CancellationToken.None).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task PerformanceBehavior_Should_Handle_100_Concurrent_Requests()
    {
        var logger = Substitute.For<ILogger<PerformanceBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new PerformanceBehaviorOptions());
        var behavior = new PerformanceBehavior<TestCommand, Result>(logger, opts);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var tasks = Enumerable.Range(0, 100).Select(i =>
            behavior.Handle(new TestCommand($"perf-{i}"), next, CancellationToken.None).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task CachingBehavior_Should_Handle_Concurrent_Cache_Misses()
    {
        var logger = Substitute.For<ILogger<CachingBehavior<CacheableQuery, Result<string>>>>();
        var opts = Options.Create(new CachingBehaviorOptions());

        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        var sp = services.BuildServiceProvider();
        var cache = sp.GetRequiredService<IDistributedCache>();

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(logger, opts, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("cached-value"));

        var tasks = Enumerable.Range(0, 50).Select(i =>
            behavior.Handle(new CacheableQuery(i), next, CancellationToken.None).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }

    [Fact]
    public async Task FullPipeline_Should_Handle_100_Concurrent_With_All_Behaviors()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddMediantLogging();
        services.AddMediantPerformanceMonitoring();
        services.AddMediantUnhandledExceptions();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(0, 100).Select(i =>
            mediator.Send(new TestCommand($"full-pipeline-{i}")).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }
}
