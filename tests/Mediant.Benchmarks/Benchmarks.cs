using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using QorpeAbstractions = Mediant.Abstractions;
using QorpeResults = Mediant.Results;
using Mediant.DependencyInjection;

namespace Mediant.Benchmarks;

// ===== QORPE TYPES =====
// Separate command types for 0-behavior vs N-behavior benchmarks to ensure
// independent static generic fields (pipeline probe cache per type)
public sealed class QorpePingCommand : QorpeAbstractions.ICommand<QorpeResults.Result> { }
public sealed class QorpePingCommandHandler : QorpeAbstractions.ICommandHandler<QorpePingCommand>
{
    public ValueTask<QorpeResults.Result> Handle(QorpePingCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result.Success());
}
public sealed class QorpeBehaviorCommand : QorpeAbstractions.ICommand<QorpeResults.Result> { }
public sealed class QorpeBehaviorCommandHandler : QorpeAbstractions.ICommandHandler<QorpeBehaviorCommand>
{
    public ValueTask<QorpeResults.Result> Handle(QorpeBehaviorCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result.Success());
}
public sealed class QorpeQueryCommand : QorpeAbstractions.IQuery<QorpeResults.Result<int>> { }
public sealed class QorpeQueryHandler : QorpeAbstractions.IQueryHandler<QorpeQueryCommand, QorpeResults.Result<int>>
{
    public ValueTask<QorpeResults.Result<int>> Handle(QorpeQueryCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(QorpeResults.Result<int>.Success(42));
}
public sealed class QorpePingNotification : QorpeAbstractions.INotification { }
public sealed class QorpePingNotificationHandler : QorpeAbstractions.INotificationHandler<QorpePingNotification>
{
    public ValueTask Handle(QorpePingNotification notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
public sealed class QorpePassthroughBehavior<TRequest, TResponse> : QorpeAbstractions.IPipelineBehavior<TRequest, TResponse>
    where TRequest : QorpeAbstractions.IRequest<TResponse>
{
    public ValueTask<TResponse> Handle(TRequest request, QorpeAbstractions.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== MEDIATR TYPES =====
public sealed class MediatRPingRequest : global::MediatR.IRequest<global::MediatR.Unit> { }
public sealed class MediatRPingRequestHandler : global::MediatR.IRequestHandler<MediatRPingRequest, global::MediatR.Unit>
{
    public Task<global::MediatR.Unit> Handle(MediatRPingRequest request, CancellationToken cancellationToken)
        => Task.FromResult(global::MediatR.Unit.Value);
}
public sealed class MediatRQueryRequest : global::MediatR.IRequest<int> { }
public sealed class MediatRQueryRequestHandler : global::MediatR.IRequestHandler<MediatRQueryRequest, int>
{
    public Task<int> Handle(MediatRQueryRequest request, CancellationToken cancellationToken)
        => Task.FromResult(42);
}
public sealed class MediatRPingNotification : global::MediatR.INotification { }
public sealed class MediatRPingNotificationHandler : global::MediatR.INotificationHandler<MediatRPingNotification>
{
    public Task Handle(MediatRPingNotification notification, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
public sealed class MediatRPassthroughBehavior<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
    where TRequest : global::MediatR.IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next();
}

// ===== COMPREHENSIVE BENCHMARK =====
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MediatorBenchmarks
{
    // Send — 0-behavior (uses QorpePingCommand for independent static cache)
    private QorpeAbstractions.IMediator _qorpe0 = null!;
    private global::MediatR.IMediator _mediatR0 = null!;

    // Send — behavior scaling (uses QorpeBehaviorCommand): 1, 2, 4, 8, 16, 32
    private QorpeAbstractions.IMediator _qorpe1B = null!;
    private QorpeAbstractions.IMediator _qorpe2B = null!;
    private QorpeAbstractions.IMediator _qorpe4B = null!;
    private QorpeAbstractions.IMediator _qorpe8B = null!;
    private QorpeAbstractions.IMediator _qorpe16B = null!;
    private QorpeAbstractions.IMediator _qorpe32B = null!;
    private global::MediatR.IMediator _mediatR1B = null!;
    private global::MediatR.IMediator _mediatR2B = null!;
    private global::MediatR.IMediator _mediatR4B = null!;
    private global::MediatR.IMediator _mediatR8B = null!;
    private global::MediatR.IMediator _mediatR16B = null!;
    private global::MediatR.IMediator _mediatR32B = null!;

    // Publish — notification handler scaling: 1, 10, 50, 100
    private QorpeAbstractions.IMediator _qorpeN1 = null!;
    private QorpeAbstractions.IMediator _qorpeN10 = null!;
    private QorpeAbstractions.IMediator _qorpeN50 = null!;
    private QorpeAbstractions.IMediator _qorpeN100 = null!;
    private global::MediatR.IMediator _mediatRN1 = null!;
    private global::MediatR.IMediator _mediatRN10 = null!;
    private global::MediatR.IMediator _mediatRN50 = null!;
    private global::MediatR.IMediator _mediatRN100 = null!;

    private readonly QorpePingCommand _qCmd = new();
    private readonly QorpeBehaviorCommand _qBehCmd = new();
    private readonly QorpeQueryCommand _qQuery = new();
    private readonly QorpePingNotification _qNotif = new();
    private readonly MediatRPingRequest _mCmd = new();
    private readonly MediatRQueryRequest _mQuery = new();
    private readonly MediatRPingNotification _mNotif = new();

    [GlobalSetup]
    public void Setup()
    {
        var asm = typeof(MediatorBenchmarks).Assembly;

        _qorpe0 = BuildQorpe(asm, 0, 0);
        _qorpe1B = BuildQorpe(asm, 1, 0);
        _qorpe2B = BuildQorpe(asm, 2, 0);
        _qorpe4B = BuildQorpe(asm, 4, 0);
        _qorpe8B = BuildQorpe(asm, 8, 0);
        _qorpe16B = BuildQorpe(asm, 16, 0);
        _qorpe32B = BuildQorpe(asm, 32, 0);
        _qorpeN1 = BuildQorpe(asm, 0, 1);
        _qorpeN10 = BuildQorpe(asm, 0, 10);
        _qorpeN50 = BuildQorpe(asm, 0, 50);
        _qorpeN100 = BuildQorpe(asm, 0, 100);

        _mediatR0 = BuildMediatR(asm, 0, 0);
        _mediatR1B = BuildMediatR(asm, 1, 0);
        _mediatR2B = BuildMediatR(asm, 2, 0);
        _mediatR4B = BuildMediatR(asm, 4, 0);
        _mediatR8B = BuildMediatR(asm, 8, 0);
        _mediatR16B = BuildMediatR(asm, 16, 0);
        _mediatR32B = BuildMediatR(asm, 32, 0);
        _mediatRN1 = BuildMediatR(asm, 0, 1);
        _mediatRN10 = BuildMediatR(asm, 0, 10);
        _mediatRN50 = BuildMediatR(asm, 0, 50);
        _mediatRN100 = BuildMediatR(asm, 0, 100);

        // Warmup — behavior benchmarks use QorpeBehaviorCommand (separate generic static fields)
        _qorpe0.Send(_qCmd).AsTask().Wait();
        _qorpe1B.Send(_qBehCmd).AsTask().Wait();
        _qorpe2B.Send(_qBehCmd).AsTask().Wait();
        _qorpe4B.Send(_qBehCmd).AsTask().Wait();
        _qorpe8B.Send(_qBehCmd).AsTask().Wait();
        _qorpe16B.Send(_qBehCmd).AsTask().Wait();
        _qorpe32B.Send(_qBehCmd).AsTask().Wait();
        _qorpeN1.Publish(_qNotif).AsTask().Wait();
        _qorpeN10.Publish(_qNotif).AsTask().Wait();
        _qorpeN50.Publish(_qNotif).AsTask().Wait();
        _qorpeN100.Publish(_qNotif).AsTask().Wait();
        _mediatR0.Send(_mCmd).Wait();
        _mediatR1B.Send(_mCmd).Wait();
        _mediatR2B.Send(_mCmd).Wait();
        _mediatR4B.Send(_mCmd).Wait();
        _mediatR8B.Send(_mCmd).Wait();
        _mediatR16B.Send(_mCmd).Wait();
        _mediatR32B.Send(_mCmd).Wait();
        _mediatRN1.Publish(_mNotif).Wait();
        _mediatRN10.Publish(_mNotif).Wait();
        _mediatRN50.Publish(_mNotif).Wait();
        _mediatRN100.Publish(_mNotif).Wait();
    }

    private static QorpeAbstractions.IMediator BuildQorpe(System.Reflection.Assembly asm, int behaviors, int notifHandlers)
    {
        var s = new ServiceCollection();
        s.AddQorpeMediator(o => o.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < behaviors; i++)
            s.AddTransient(typeof(QorpeAbstractions.IPipelineBehavior<,>), typeof(QorpePassthroughBehavior<,>));
        for (int i = 1; i < notifHandlers; i++) // 1 already from assembly scan
            s.AddTransient<QorpeAbstractions.INotificationHandler<QorpePingNotification>, QorpePingNotificationHandler>();
        return s.BuildServiceProvider().GetRequiredService<QorpeAbstractions.IMediator>();
    }

    private static global::MediatR.IMediator BuildMediatR(System.Reflection.Assembly asm, int behaviors, int notifHandlers)
    {
        var s = new ServiceCollection();
        s.AddMediatR(c => c.RegisterServicesFromAssembly(asm));
        for (int i = 0; i < behaviors; i++)
            s.AddTransient(typeof(global::MediatR.IPipelineBehavior<,>), typeof(MediatRPassthroughBehavior<,>));
        for (int i = 1; i < notifHandlers; i++)
            s.AddTransient<global::MediatR.INotificationHandler<MediatRPingNotification>, MediatRPingNotificationHandler>();
        return s.BuildServiceProvider().GetRequiredService<global::MediatR.IMediator>();
    }

    // ===== SEND (behavior scaling: 0, 1, 2, 4, 8, 16, 32) =====
    [Benchmark(Description = "Qorpe Send (0 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_0() => _qorpe0.Send(_qCmd);
    [Benchmark(Description = "MediatR Send (0 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_0() => _mediatR0.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (1 behavior)")]
    public ValueTask<QorpeResults.Result> Q_Send_1() => _qorpe1B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (1 behavior)")]
    public Task<global::MediatR.Unit> M_Send_1() => _mediatR1B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (2 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_2() => _qorpe2B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (2 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_2() => _mediatR2B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (4 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_4() => _qorpe4B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (4 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_4() => _mediatR4B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (8 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_8() => _qorpe8B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (8 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_8() => _mediatR8B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (16 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_16() => _qorpe16B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (16 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_16() => _mediatR16B.Send(_mCmd);

    [Benchmark(Description = "Qorpe Send (32 behaviors)")]
    public ValueTask<QorpeResults.Result> Q_Send_32() => _qorpe32B.Send(_qBehCmd);
    [Benchmark(Description = "MediatR Send (32 behaviors)")]
    public Task<global::MediatR.Unit> M_Send_32() => _mediatR32B.Send(_mCmd);

    // ===== QUERY =====
    [Benchmark(Description = "Qorpe Query (Result<int>)")]
    public ValueTask<QorpeResults.Result<int>> Q_Query() => _qorpe0.Send(_qQuery);
    [Benchmark(Description = "MediatR Query (int)")]
    public Task<int> M_Query() => _mediatR0.Send(_mQuery);

    // ===== PUBLISH (handler scaling: 1, 10, 50, 100) =====
    [Benchmark(Description = "Qorpe Publish (1 handler)")]
    public ValueTask Q_Pub_1() => _qorpeN1.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (1 handler)")]
    public Task M_Pub_1() => _mediatRN1.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (10 handlers)")]
    public ValueTask Q_Pub_10() => _qorpeN10.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (10 handlers)")]
    public Task M_Pub_10() => _mediatRN10.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (50 handlers)")]
    public ValueTask Q_Pub_50() => _qorpeN50.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (50 handlers)")]
    public Task M_Pub_50() => _mediatRN50.Publish(_mNotif);

    [Benchmark(Description = "Qorpe Publish (100 handlers)")]
    public ValueTask Q_Pub_100() => _qorpeN100.Publish(_qNotif);
    [Benchmark(Description = "MediatR Publish (100 handlers)")]
    public Task M_Pub_100() => _mediatRN100.Publish(_mNotif);
}
