using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.DependencyInjection;
using Qorpe.Mediator.Results;

namespace Qorpe.Mediator.UnitTests.Core;

/// <summary>
/// Verifies open-generic pipeline/stream behaviors registered via
/// <see cref="MediatorOptions.AddOpenBehavior"/> apply to every request type and run in order.
/// </summary>
public class OpenGenericBehaviorTests
{
    [Fact]
    public async Task OpenBehavior_Applies_To_Multiple_Request_Types()
    {
        var log = new CallLog();
        var sp = Build(log, cfg => cfg.AddOpenBehavior(typeof(OpenLogging<,>)));
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new OpenCmd());
        await mediator.Send(new OpenQry());

        log.Entries.Should().Contain("log:OpenCmd").And.Contain("log:OpenQry",
            "a single open-generic behavior must wrap every request type");
    }

    [Fact]
    public async Task Multiple_OpenBehaviors_Run_In_BehaviorOrder()
    {
        var log = new CallLog();
        var sp = Build(log, cfg =>
        {
            // Registered out of order on purpose; IBehaviorOrder must drive execution order.
            cfg.AddOpenBehavior(typeof(OpenSecond<,>));
            cfg.AddOpenBehavior(typeof(OpenFirst<,>));
        });
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new OpenCmd());

        log.Entries.Should().ContainInOrder("first:in", "second:in");
    }

    [Fact]
    public async Task OpenStreamBehavior_Wraps_Stream_Requests()
    {
        var log = new CallLog();
        var sp = Build(log, cfg => cfg.AddOpenStreamBehavior(typeof(OpenStreamLogging<,>)));
        var mediator = sp.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var i in mediator.CreateStream(new OpenStreamReq()))
        {
            items.Add(i);
        }

        items.Should().Equal(1, 2, 3);
        log.Entries.Should().Contain("stream:OpenStreamReq");
    }

    [Fact]
    public void AddOpenBehavior_With_Closed_Type_Throws()
    {
        var act = () => new ServiceCollection().AddQorpeMediator(cfg =>
            cfg.AddOpenBehavior(typeof(OpenLogging<OpenCmd, Result>)));
        act.Should().Throw<ArgumentException>().WithMessage("*open generic type definition*");
    }

    [Fact]
    public void AddOpenBehavior_With_Non_Behavior_Type_Throws()
    {
        var act = () => new ServiceCollection().AddQorpeMediator(cfg =>
            cfg.AddOpenBehavior(typeof(NotABehavior<,>)));
        act.Should().Throw<ArgumentException>().WithMessage("*must implement*");
    }

    private static ServiceProvider Build(CallLog log, Action<MediatorOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddQorpeMediator(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(OpenGenericBehaviorTests).Assembly);
            configure(cfg);
        });
        return services.BuildServiceProvider();
    }
}

public sealed class CallLog
{
    private readonly object _gate = new();
    private readonly List<string> _entries = new();
    public void Add(string e) { lock (_gate) { _entries.Add(e); } }
    public IReadOnlyList<string> Entries { get { lock (_gate) { return _entries.ToList(); } } }
}

public sealed record OpenCmd : ICommand<Result>;
internal sealed class OpenCmdHandler : ICommandHandler<OpenCmd>
{
    public ValueTask<Result> Handle(OpenCmd request, CancellationToken cancellationToken) => ValueTask.FromResult(Result.Success());
}

public sealed record OpenQry : IQuery<Result<int>>;
internal sealed class OpenQryHandler : IQueryHandler<OpenQry, Result<int>>
{
    public ValueTask<Result<int>> Handle(OpenQry request, CancellationToken cancellationToken) => ValueTask.FromResult(Result<int>.Success(7));
}

public sealed class OpenLogging<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly CallLog _log;
    public OpenLogging(CallLog log) => _log = log;
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _log.Add($"log:{typeof(TRequest).Name}");
        return next();
    }
}

public sealed class OpenFirst<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    private readonly CallLog _log;
    public OpenFirst(CallLog log) => _log = log;
    public int Order => 1;
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _log.Add("first:in");
        return next();
    }
}

public sealed class OpenSecond<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    private readonly CallLog _log;
    public OpenSecond(CallLog log) => _log = log;
    public int Order => 2;
    public ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _log.Add("second:in");
        return next();
    }
}

// Implements an unrelated open generic interface to exercise AddOpenBehavior validation.
public sealed class NotABehavior<TA, TB> : IComparer<TA>
{
    public int Compare(TA? x, TA? y) => 0;
}

public sealed record OpenStreamReq : IStreamRequest<int>;
internal sealed class OpenStreamReqHandler : IStreamRequestHandler<OpenStreamReq, int>
{
    public async IAsyncEnumerable<int> Handle(OpenStreamReq request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 1; i <= 3; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}

public sealed class OpenStreamLogging<TRequest, TResponse> : IStreamPipelineBehavior<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    private readonly CallLog _log;
    public OpenStreamLogging(CallLog log) => _log = log;
    public IAsyncEnumerable<TResponse> Handle(TRequest request, StreamHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        _log.Add($"stream:{typeof(TRequest).Name}");
        return next();
    }
}
