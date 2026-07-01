using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Core;

/// <summary>
/// Verifies that pipeline behaviors execute in the correct order
/// based on IBehaviorOrder.Order values.
/// </summary>
public class BehaviorOrderingTests
{
    // Shared execution log — each behavior appends its name
    private static readonly List<string> ExecutionLog = new();

    // --- Test command & handler ---
    public sealed record OrderTestCommand(string Data) : ICommand<Result>;
    public sealed class OrderTestCommandHandler : ICommandHandler<OrderTestCommand>
    {
        public ValueTask<Result> Handle(OrderTestCommand request, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Handler");
            return new(Result.Success());
        }
    }

    // --- Ordered behaviors (implement IBehaviorOrder) ---
    public sealed class BehaviorOrder100<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IRequest<TResponse>
    {
        public int Order => 100;
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Order100");
            return await next().ConfigureAwait(false);
        }
    }

    public sealed class BehaviorOrder300<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IRequest<TResponse>
    {
        public int Order => 300;
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Order300");
            return await next().ConfigureAwait(false);
        }
    }

    public sealed class BehaviorOrder500<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IRequest<TResponse>
    {
        public int Order => 500;
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Order500");
            return await next().ConfigureAwait(false);
        }
    }

    public sealed class BehaviorOrder900<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IRequest<TResponse>
    {
        public int Order => 900;
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Order900");
            return await next().ConfigureAwait(false);
        }
    }

    // --- Unordered behavior (no IBehaviorOrder) ---
    public sealed class UnorderedBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Unordered");
            return await next().ConfigureAwait(false);
        }
    }

    private void ResetLog() => ExecutionLog.Clear();

    [Fact]
    public async Task Behaviors_Should_Execute_In_Ascending_Order()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderingTests).Assembly));

        // Register in reverse order — sorting should fix this
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder900<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder100<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder500<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder300<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        await mediator.Send(new OrderTestCommand("test"));

        ExecutionLog.Should().Equal("Order100", "Order300", "Order500", "Order900", "Handler");
    }

    [Fact]
    public async Task Unordered_Behaviors_Should_Get_Default_Position()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderingTests).Assembly));

        // Unordered gets int.MaxValue/2, so it should be after all ordered behaviors
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnorderedBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder100<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder900<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        await mediator.Send(new OrderTestCommand("test"));

        ExecutionLog.Should().Equal("Order100", "Order900", "Unordered", "Handler");
    }

    [Fact]
    public async Task Same_Order_Value_Should_Maintain_Registration_Order()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderingTests).Assembly));

        // Two behaviors with same order — registration order preserved (stable sort)
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder100<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder500<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder300<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(BehaviorOrder500<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        await mediator.Send(new OrderTestCommand("test"));

        // Both Order500 entries should appear in registration order
        ExecutionLog.Should().Equal("Order100", "Order300", "Order500", "Order500", "Handler");
    }

    [Fact]
    public async Task No_Ordered_Behaviors_Should_Maintain_Registration_Order()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(BehaviorOrderingTests).Assembly));

        // All unordered — registration order preserved, no sorting needed
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnorderedBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnorderedBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnorderedBehavior<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        await mediator.Send(new OrderTestCommand("test"));

        ExecutionLog.Should().Equal("Unordered", "Unordered", "Unordered", "Handler");
    }
}
