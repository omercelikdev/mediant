using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Core;

/// <summary>
/// Tests for the per-container pipeline probe cache optimization.
/// Verifies that the fast path (handler-only) is taken when appropriate
/// and that the full pipeline path is taken when behaviors/processors exist.
/// </summary>
public class PipelineProbeCacheTests
{
    // ===== Types for testing =====
    public sealed record FastPathCommand(string Data) : ICommand<Result>;
    public sealed class FastPathCommandHandler : ICommandHandler<FastPathCommand>
    {
        public static int CallCount;
        public ValueTask<Result> Handle(FastPathCommand request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return new ValueTask<Result>(Result.Success());
        }
    }

    public sealed record FastPathQuery(int Id) : IQuery<Result<string>>;
    public sealed class FastPathQueryHandler : IQueryHandler<FastPathQuery, Result<string>>
    {
        public static int CallCount;
        public ValueTask<Result<string>> Handle(FastPathQuery request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return new ValueTask<Result<string>>(Result<string>.Success($"Item-{request.Id}"));
        }
    }

    public sealed class CountingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public static int ExecutionCount;
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ExecutionCount);
            return await next().ConfigureAwait(false);
        }
    }

    public sealed class CountingPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
        where TRequest : notnull
    {
        public static int ExecutionCount;
        public ValueTask Process(TRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ExecutionCount);
            return ValueTask.CompletedTask;
        }
    }

    public sealed class CountingPostProcessor<TRequest, TResponse> : IRequestPostProcessor<TRequest, TResponse>
        where TRequest : notnull
    {
        public static int ExecutionCount;
        public ValueTask Process(TRequest request, TResponse response, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ExecutionCount);
            return ValueTask.CompletedTask;
        }
    }

    // ===== Fast path tests =====

    [Fact]
    public async Task HandlerOnly_Should_Take_FastPath_And_Return_Correct_Result()
    {
        FastPathCommandHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new FastPathCommand("test"));

        result.IsSuccess.Should().BeTrue();
        FastPathCommandHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task HandlerOnly_Should_Work_Correctly_On_Repeated_Calls()
    {
        FastPathCommandHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // First call populates cache, subsequent calls use fast path
        for (int i = 0; i < 10; i++)
        {
            var result = await mediator.Send(new FastPathCommand($"call-{i}"));
            result.IsSuccess.Should().BeTrue();
        }

        FastPathCommandHandler.CallCount.Should().Be(10);
    }

    [Fact]
    public async Task Query_HandlerOnly_Should_Take_FastPath()
    {
        FastPathQueryHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new FastPathQuery(42));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Item-42");
        FastPathQueryHandler.CallCount.Should().Be(1);
    }

    // ===== Behavior pipeline tests =====

    [Fact]
    public async Task WithBehaviors_Should_NOT_Take_FastPath()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Call multiple times
        for (int i = 0; i < 5; i++)
        {
            await mediator.Send(new FastPathCommand($"call-{i}"));
        }

        FastPathCommandHandler.CallCount.Should().Be(5);
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(5,
            "behavior must execute on every call, cache must not skip it");
    }

    [Fact]
    public async Task WithPreProcessor_Should_NOT_Take_FastPath()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingPreProcessor<FastPathCommand>.ExecutionCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services.AddTransient(typeof(IRequestPreProcessor<>), typeof(CountingPreProcessor<>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        for (int i = 0; i < 3; i++)
        {
            await mediator.Send(new FastPathCommand($"call-{i}"));
        }

        FastPathCommandHandler.CallCount.Should().Be(3);
        CountingPreProcessor<FastPathCommand>.ExecutionCount.Should().Be(3,
            "pre-processor must execute on every call");
    }

    [Fact]
    public async Task WithPostProcessor_Should_NOT_Take_FastPath()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingPostProcessor<FastPathCommand, Result>.ExecutionCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services.AddTransient(typeof(IRequestPostProcessor<,>), typeof(CountingPostProcessor<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        for (int i = 0; i < 3; i++)
        {
            await mediator.Send(new FastPathCommand($"call-{i}"));
        }

        FastPathCommandHandler.CallCount.Should().Be(3);
        CountingPostProcessor<FastPathCommand, Result>.ExecutionCount.Should().Be(3,
            "post-processor must execute on every call");
    }

    // ===== Per-container isolation tests =====

    [Fact]
    public async Task Different_Containers_Must_Have_Independent_Cache()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;

        // Container 1: NO behaviors (handler-only)
        var services1 = new ServiceCollection();
        services1.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        var mediator1 = services1.BuildServiceProvider().GetRequiredService<IMediator>();

        // Container 2: WITH behaviors
        var services2 = new ServiceCollection();
        services2.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services2.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));
        services2.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));
        var mediator2 = services2.BuildServiceProvider().GetRequiredService<IMediator>();

        // Warm up container 1 (should cache as handler-only)
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;
        await mediator1.Send(new FastPathCommand("no-behaviors"));
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(0);

        // Container 2 must still execute behaviors despite container 1's cache
        await mediator2.Send(new FastPathCommand("with-behaviors"));
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(2,
            "container 2 has 2 behaviors — must NOT be affected by container 1's handler-only cache");
    }

    [Fact]
    public async Task Different_Containers_Reverse_Order_Must_Have_Independent_Cache()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;

        // Container 1: WITH behaviors (warmed first)
        var services1 = new ServiceCollection();
        services1.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services1.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));
        var mediator1 = services1.BuildServiceProvider().GetRequiredService<IMediator>();

        // Container 2: NO behaviors
        var services2 = new ServiceCollection();
        services2.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        var mediator2 = services2.BuildServiceProvider().GetRequiredService<IMediator>();

        // Warm up container 1 (should cache as has-pipeline)
        await mediator1.Send(new FastPathCommand("with-behaviors"));
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(1);

        // Container 2 must still work without behaviors
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;
        await mediator2.Send(new FastPathCommand("no-behaviors"));
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(0,
            "container 2 has no behaviors — must NOT be affected by container 1's cache");
    }

    // ===== Scoped DI tests =====

    [Fact]
    public async Task Scoped_ServiceProviders_Should_Share_Cache()
    {
        FastPathCommandHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var rootSp = services.BuildServiceProvider();

        // Call from multiple scopes — all should use the same cache
        for (int i = 0; i < 5; i++)
        {
            using var scope = rootSp.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new FastPathCommand($"scope-{i}"));
            result.IsSuccess.Should().BeTrue();
        }

        FastPathCommandHandler.CallCount.Should().Be(5);
    }

    // ===== Concurrent access tests =====

    [Fact]
    public async Task Concurrent_Sends_Should_Be_ThreadSafe()
    {
        FastPathCommandHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var sp = services.BuildServiceProvider();

        const int concurrency = 100;
        var tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = sp.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new FastPathCommand($"concurrent-{idx}"));
                result.IsSuccess.Should().BeTrue();
            });
        }

        await Task.WhenAll(tasks);
        FastPathCommandHandler.CallCount.Should().Be(concurrency);
    }

    [Fact]
    public async Task Concurrent_Sends_With_Behaviors_Should_Be_ThreadSafe()
    {
        FastPathCommandHandler.CallCount = 0;
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));

        var sp = services.BuildServiceProvider();

        const int concurrency = 100;
        var tasks = new Task[concurrency];
        for (int i = 0; i < concurrency; i++)
        {
            var idx = i;
            tasks[i] = Task.Run(async () =>
            {
                using var scope = sp.CreateScope();
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new FastPathCommand($"concurrent-{idx}"));
                result.IsSuccess.Should().BeTrue();
            });
        }

        await Task.WhenAll(tasks);
        FastPathCommandHandler.CallCount.Should().Be(concurrency);
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(concurrency);
    }

    // ===== First call correctness =====

    [Fact]
    public async Task First_Call_Must_Return_Correct_Result_Before_Cache_Is_Populated()
    {
        FastPathCommandHandler.CallCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Very first call (cache is cold)
        var result = await mediator.Send(new FastPathCommand("first-ever"));

        result.IsSuccess.Should().BeTrue();
        FastPathCommandHandler.CallCount.Should().Be(1);
    }

    // ===== Mixed scenarios =====

    [Fact]
    public async Task Multiple_Request_Types_Should_Have_Independent_Cache_Entries()
    {
        FastPathCommandHandler.CallCount = 0;
        FastPathQueryHandler.CallCount = 0;
        CountingBehavior<FastPathCommand, Result>.ExecutionCount = 0;

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(PipelineProbeCacheTests).Assembly));
        // Register behavior only for commands (open generic applies to both, but that's fine for this test)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CountingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Both types go through the behavior (open generic registration)
        await mediator.Send(new FastPathCommand("cmd"));
        await mediator.Send(new FastPathQuery(1));

        FastPathCommandHandler.CallCount.Should().Be(1);
        FastPathQueryHandler.CallCount.Should().Be(1);
        // Open generic behavior applies to both
        CountingBehavior<FastPathCommand, Result>.ExecutionCount.Should().Be(1);
    }
}
