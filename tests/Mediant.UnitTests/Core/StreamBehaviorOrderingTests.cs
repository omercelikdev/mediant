using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Core;

/// <summary>
/// Verifies that stream pipeline behaviors execute in the correct order
/// based on IBehaviorOrder.Order values (added in PR #75).
/// </summary>
public class StreamBehaviorOrderingTests
{
    private static readonly List<string> ExecutionLog = new();

    public sealed record OrderedStreamRequest(int Count) : IStreamRequest<int>;
    public sealed class OrderedStreamHandler : IStreamRequestHandler<OrderedStreamRequest, int>
    {
        public async IAsyncEnumerable<int> Handle(OrderedStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Handler");
            for (int i = 0; i < request.Count; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }
    }

    public sealed class StreamBehaviorOrder100<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IStreamRequest<TResponse>
    {
        public int Order => 100;
        public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Stream100");
            return next();
        }
    }

    public sealed class StreamBehaviorOrder500<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IStreamRequest<TResponse>
    {
        public int Order => 500;
        public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Stream500");
            return next();
        }
    }

    public sealed class StreamBehaviorOrder900<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
        where TRequest : IStreamRequest<TResponse>
    {
        public int Order => 900;
        public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Stream900");
            return next();
        }
    }

    public sealed class UnorderedStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
        where TRequest : IStreamRequest<TResponse>
    {
        public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            ExecutionLog.Add("Unordered");
            return next();
        }
    }

    private void ResetLog() => ExecutionLog.Clear();

    [Fact]
    public async Task Stream_Behaviors_Should_Execute_In_Ascending_Order()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(StreamBehaviorOrderingTests).Assembly));

        // Register in reverse order
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamBehaviorOrder900<,>));
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamBehaviorOrder100<,>));
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamBehaviorOrder500<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new OrderedStreamRequest(2)))
        {
            items.Add(item);
        }

        ExecutionLog.Should().Equal("Stream100", "Stream500", "Stream900", "Handler");
        items.Should().Equal(0, 1);
    }

    [Fact]
    public async Task Stream_Mixed_Ordered_Unordered_Should_Sort_Correctly()
    {
        ResetLog();

        var services = new ServiceCollection();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(StreamBehaviorOrderingTests).Assembly));

        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(UnorderedStreamBehavior<,>));
        services.AddTransient(typeof(IStreamPipelineBehavior<,>), typeof(StreamBehaviorOrder100<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await foreach (var _ in mediator.CreateStream(new OrderedStreamRequest(1))) { }

        // Order100 first, then Unordered (default = int.MaxValue/2)
        ExecutionLog.Should().Equal("Stream100", "Unordered", "Handler");
    }
}
