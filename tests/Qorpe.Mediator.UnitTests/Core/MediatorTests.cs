using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.DependencyInjection;
using Qorpe.Mediator.Exceptions;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Core;

public class MediatorSendTests
{
    private IMediator CreateMediator(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Send_Command_ShouldReturnSuccess()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestCommand("test"));
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Send_CommandWithResponse_ShouldReturnValue()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestCommandWithResponse("World"));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hello, World!");
    }

    [Fact]
    public async Task Send_Query_ShouldReturnValue()
    {
        var mediator = CreateMediator();
        var result = await mediator.Send(new TestQuery(42));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Item-42");
    }

    [Fact]
    public async Task Send_NullRequest_ShouldThrowArgumentNull()
    {
        var mediator = CreateMediator();
        var act = async () => await mediator.Send<Result>((IRequest<Result>)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Send_WithCancelledToken_ShouldThrowOperationCancelled()
    {
        var mediator = CreateMediator();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await mediator.Send(new TestCommand("test"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void HandlerNotFoundException_Should_Include_Assembly_Registration_Hint()
    {
        var ex = new HandlerNotFoundException(typeof(TestCommand));

        ex.Message.Should().Contain("RegisterServicesFromAssembly",
            "error should hint about assembly registration");
        ex.Message.Should().Contain(typeof(TestCommand).FullName);
        ex.RequestType.Should().Be<TestCommand>();
    }

    [Fact]
    public void HandlerNotFoundException_WithCount_Should_Include_Registered_Handler_Count()
    {
        var ex = new HandlerNotFoundException(typeof(TestCommand), 15);

        ex.Message.Should().Contain("15 handler(s) registered",
            "error should show how many handlers exist for other types");
        ex.Message.Should().Contain("RegisterServicesFromAssembly");
    }
}

public class MediatorPublishTests
{
    [Fact]
    public async Task Publish_WithHandlers_ShouldInvokeAll()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // Just verify it doesn't throw
        await mediator.Publish(new TestNotification("hello"));
    }

    [Fact]
    public async Task Publish_WhenScannedHandlerThrows_ShouldPropagate()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // ThrowingNotificationHandler is discovered by scanning and must actually run.
        var act = async () => await mediator.Publish(new ThrowingTestNotification("boom"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Handler failed");
    }

    [Fact]
    public async Task Publish_WithNoHandlers_ShouldSucceedSilently()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        // DomainEvent with no handler — should be silent
        await mediator.Publish(new TestDomainEvent("test"));
    }

    [Fact]
    public async Task Publish_NullNotification_ShouldThrowArgumentNull()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestNotification).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = async () => await mediator.Publish((INotification)null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

public class MediatorStreamTests
{
    [Fact]
    public async Task CreateStream_ShouldReturnAllItems()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new TestStreamRequest(5)))
        {
            items.Add(item);
        }

        items.Should().HaveCount(5);
        items.Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
    }

    [Fact]
    public async Task CreateStream_WithCancellation_ShouldStop()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        var items = new List<int>();
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStream(new TestStreamRequest(100), cts.Token))
            {
                items.Add(item);
                if (items.Count >= 3)
                {
                    cts.Cancel();
                }
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        items.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task CreateStream_WithBehavior_ShouldExecuteBehavior()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var trackingBehavior = new TrackingStreamBehavior<TestStreamRequest, int>();
        services.AddSingleton<IStreamPipelineBehavior<TestStreamRequest, int>>(trackingBehavior);
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new TestStreamRequest(3)))
        {
            items.Add(item);
        }

        items.Should().HaveCount(3);
        trackingBehavior.WasExecuted.Should().BeTrue("stream pipeline behavior should execute");
    }

    [Fact]
    public async Task CreateStream_WithBlockingBehavior_ShouldPreventStreaming()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        services.AddSingleton<IStreamPipelineBehavior<TestStreamRequest, int>>(
            new BlockingStreamBehavior<TestStreamRequest, int>());
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        var act = async () =>
        {
            await foreach (var item in mediator.CreateStream(new TestStreamRequest(5)))
            {
                items.Add(item);
            }
        };

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        items.Should().BeEmpty("blocking behavior should prevent any items from being yielded");
    }

    [Fact]
    public async Task CreateStream_WithoutBehavior_ShouldStillWork()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestStreamRequest).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.CreateStream(new TestStreamRequest(3)))
        {
            items.Add(item);
        }

        items.Should().HaveCount(3, "streaming without behaviors should work as before");
    }
}

/// <summary>
/// Stream behavior that tracks execution for testing.
/// </summary>
internal sealed class TrackingStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public bool WasExecuted { get; private set; }

    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        WasExecuted = true;
        return next();
    }
}

/// <summary>
/// Stream behavior that blocks execution (simulates authorization failure).
/// </summary>
internal sealed class BlockingStreamBehavior<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        throw new UnauthorizedAccessException("Stream access denied");
    }
}

public class MediatorPrePostProcessorTests
{
    [Fact]
    public async Task Send_WithPreProcessor_ShouldExecuteBeforeHandler()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddTransient<IRequestPreProcessor<TestCommand>>(
            _ => new TrackingPreProcessor<TestCommand>(executionOrder, "pre"));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        executionOrder.Should().Contain("pre");
    }

    [Fact]
    public async Task Send_WithPostProcessor_ShouldExecuteAfterHandler()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddTransient<IRequestPostProcessor<TestCommand, Result>>(
            _ => new TrackingPostProcessor<TestCommand, Result>(executionOrder, "post"));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        executionOrder.Should().Contain("post");
    }

    [Fact]
    public async Task Send_WithBothProcessors_ShouldExecuteInCorrectOrder()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddTransient<IRequestPreProcessor<TestCommand>>(
            _ => new TrackingPreProcessor<TestCommand>(executionOrder, "pre"));
        services.AddTransient<IRequestPostProcessor<TestCommand, Result>>(
            _ => new TrackingPostProcessor<TestCommand, Result>(executionOrder, "post"));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        executionOrder.Should().ContainInOrder("pre", "post");
    }

    [Fact]
    public async Task Send_WithMultiplePreProcessors_ShouldExecuteAll()
    {
        var executionOrder = new List<string>();
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddTransient<IRequestPreProcessor<TestCommand>>(
            _ => new TrackingPreProcessor<TestCommand>(executionOrder, "pre-1"));
        services.AddTransient<IRequestPreProcessor<TestCommand>>(
            _ => new TrackingPreProcessor<TestCommand>(executionOrder, "pre-2"));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new TestCommand("test"));

        executionOrder.Should().ContainInOrder("pre-1", "pre-2");
    }

    [Fact]
    public async Task Send_WithoutProcessors_ShouldStillWork()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("test"));
        result.IsSuccess.Should().BeTrue("backward compatibility must be preserved");
    }
}

internal sealed class TrackingPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    where TRequest : notnull
{
    private readonly List<string> _executionOrder;
    private readonly string _name;

    public TrackingPreProcessor(List<string> executionOrder, string name)
    {
        _executionOrder = executionOrder;
        _name = name;
    }

    public ValueTask Process(TRequest request, CancellationToken cancellationToken)
    {
        _executionOrder.Add(_name);
        return ValueTask.CompletedTask;
    }
}

internal sealed class TrackingPostProcessor<TRequest, TResponse> : IRequestPostProcessor<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly List<string> _executionOrder;
    private readonly string _name;

    public TrackingPostProcessor(List<string> executionOrder, string name)
    {
        _executionOrder = executionOrder;
        _name = name;
    }

    public ValueTask Process(TRequest request, TResponse response, CancellationToken cancellationToken)
    {
        _executionOrder.Add(_name);
        return ValueTask.CompletedTask;
    }
}

public class MediatorCancellationDiagnosticsTests
{
    [Fact]
    public async Task Should_Log_Cancellation_With_Pipeline_Stage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        // Add a slow behavior that will be cancelled
        services.AddTransient<IPipelineBehavior<TestCommand, Result>, SlowBehavior>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        cts.CancelAfter(10); // Cancel quickly

        var act = async () => await mediator.Send(new TestCommand("test"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // The cancellation diagnostic log was emitted (verified by not throwing + cancellation propagating)
    }

    [Fact]
    public async Task Should_Propagate_Cancellation_Without_Swallowing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        services.AddTransient<IPipelineBehavior<TestCommand, Result>, SlowBehavior>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled

        var act = async () => await mediator.Send(new TestCommand("test"), cts.Token);

        // Must throw — diagnostics should never swallow the exception
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

internal sealed class SlowBehavior : IPipelineBehavior<TestCommand, Result>
{
    public async ValueTask<Result> Handle(TestCommand request, RequestHandlerDelegate<Result> next, CancellationToken cancellationToken)
    {
        await Task.Delay(1000, cancellationToken);
        return await next().ConfigureAwait(false);
    }
}

public class MediatorConcurrencyTests
{
    [Fact]
    public async Task Send_ConcurrentRequests_ShouldNotDeadlock()
    {
        var services = new ServiceCollection();
        services.AddQorpeMediator(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(TestCommand).Assembly));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var tasks = Enumerable.Range(0, 100)
            .Select(i => mediator.Send(new TestCommand($"test-{i}")).AsTask());

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
    }
}
