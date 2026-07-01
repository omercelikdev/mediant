using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Core;

/// <summary>
/// Critical verification: behaviors MUST actually execute, not be skipped.
/// This test class exists specifically to catch the behavior-cache-bypass bug.
/// </summary>
public class BehaviorExecutionVerificationTests
{
    // Tracking behavior that records execution
    public sealed class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public static int ExecutionCount;

        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ExecutionCount);
            return await next().ConfigureAwait(false);
        }
    }

    public sealed record TrackableCommand(string Data) : ICommand<Result>;
    public sealed class TrackableCommandHandler : ICommandHandler<TrackableCommand>
    {
        public static int HandlerCallCount;
        public ValueTask<Result> Handle(TrackableCommand request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref HandlerCallCount);
            return new ValueTask<Result>(Result.Success());
        }
    }

    [Fact]
    public async Task Behaviors_Must_Actually_Execute_Not_Be_Skipped()
    {
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount = 0;
        TrackableCommandHandler.HandlerCallCount = 0;

        var services = new ServiceCollection();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorExecutionVerificationTests).Assembly));
        // Register 3 behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TrackableCommand("test"));

        result.IsSuccess.Should().BeTrue();
        TrackableCommandHandler.HandlerCallCount.Should().Be(1, "handler must be called exactly once");
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount.Should().Be(3,
            "all 3 behaviors must execute — if this is 0, behaviors are being skipped!");
    }

    [Fact]
    public async Task Behaviors_Execute_On_Every_Call_Not_Just_First()
    {
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount = 0;
        TrackableCommandHandler.HandlerCallCount = 0;

        var services = new ServiceCollection();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorExecutionVerificationTests).Assembly));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Call 5 times
        for (int i = 0; i < 5; i++)
        {
            await mediator.Send(new TrackableCommand($"call-{i}"));
        }

        TrackableCommandHandler.HandlerCallCount.Should().Be(5);
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount.Should().Be(5,
            "behavior must execute on EVERY call, not just the first");
    }

    [Fact]
    public async Task No_Behaviors_Registered_Should_Call_Handler_Directly()
    {
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount = 0;
        TrackableCommandHandler.HandlerCallCount = 0;

        var services = new ServiceCollection();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorExecutionVerificationTests).Assembly));
        // NO behaviors registered

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TrackableCommand("no-behaviors"));

        result.IsSuccess.Should().BeTrue();
        TrackableCommandHandler.HandlerCallCount.Should().Be(1);
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount.Should().Be(0,
            "no behaviors registered means zero behavior executions");
    }

    [Fact]
    public async Task Different_DI_Containers_Must_Have_Independent_Behavior_Resolution()
    {
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount = 0;

        // Container 1: NO behaviors
        var services1 = new ServiceCollection();
        services1.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorExecutionVerificationTests).Assembly));
        var mediator1 = services1.BuildServiceProvider().GetRequiredService<IMediator>();

        // Container 2: WITH 2 behaviors
        var services2 = new ServiceCollection();
        services2.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorExecutionVerificationTests).Assembly));
        services2.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));
        services2.AddTransient(typeof(IPipelineBehavior<,>), typeof(TrackingBehavior<,>));
        var mediator2 = services2.BuildServiceProvider().GetRequiredService<IMediator>();

        TrackingBehavior<TrackableCommand, Result>.ExecutionCount = 0;

        // Call mediator1 first (no behaviors)
        await mediator1.Send(new TrackableCommand("no-beh"));
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount.Should().Be(0);

        // Call mediator2 (WITH behaviors) — must NOT be affected by mediator1's state
        await mediator2.Send(new TrackableCommand("with-beh"));
        TrackingBehavior<TrackableCommand, Result>.ExecutionCount.Should().Be(2,
            "mediator2 has 2 behaviors registered — they MUST execute regardless of mediator1's state");
    }
}
