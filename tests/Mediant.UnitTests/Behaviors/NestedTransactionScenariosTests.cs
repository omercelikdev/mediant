using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

/// <summary>
/// Comprehensive nested transaction scenarios proving:
/// - Only 1 BeginTransaction regardless of nesting depth
/// - Any failure at any depth rolls back everything
/// - Successful inner commands don't commit independently
/// - PostCommit tasks only run after outermost commit
/// - 3+ levels deep works correctly
/// </summary>
public class NestedTransactionScenariosTests
{
    private static readonly List<string> EventLog = new();
    private static void ResetLog() => EventLog.Clear();

    // --- Each scenario uses unique command types to avoid duplicate handler detection ---

    // Scenario 1: 3 levels deep, all succeed
    [Transactional] public sealed record S1_Level1(string Data) : ICommand<Result>;
    [Transactional] public sealed record S1_Level2(string Data) : ICommand<Result>;
    [Transactional] public sealed record S1_Level3(string Data) : ICommand<Result>;

    public sealed class S1_Level1Handler(IMediator m) : ICommandHandler<S1_Level1>
    {
        public async ValueTask<Result> Handle(S1_Level1 request, CancellationToken cancellationToken)
        {
            EventLog.Add("L1:start");
            var r = await m.Send(new S1_Level2("from-L1"), cancellationToken);
            EventLog.Add("L1:end");
            return r;
        }
    }
    public sealed class S1_Level2Handler(IMediator m) : ICommandHandler<S1_Level2>
    {
        public async ValueTask<Result> Handle(S1_Level2 request, CancellationToken cancellationToken)
        {
            EventLog.Add("L2:start");
            var r = await m.Send(new S1_Level3("from-L2"), cancellationToken);
            EventLog.Add("L2:end");
            return r;
        }
    }
    public sealed class S1_Level3Handler : ICommandHandler<S1_Level3>
    {
        public ValueTask<Result> Handle(S1_Level3 request, CancellationToken cancellationToken)
        {
            EventLog.Add("L3:executed");
            return new(Result.Success());
        }
    }

    // Scenario 2: Inner fails
    [Transactional] public sealed record S2_Outer(string Data) : ICommand<Result>;
    [Transactional] public sealed record S2_FailingInner(string Data) : ICommand<Result>;

    public sealed class S2_OuterHandler(IMediator m) : ICommandHandler<S2_Outer>
    {
        public async ValueTask<Result> Handle(S2_Outer request, CancellationToken cancellationToken)
        {
            EventLog.Add("S2-outer:start");
            return await m.Send(new S2_FailingInner("will-fail"), cancellationToken);
        }
    }
    public sealed class S2_FailingInnerHandler : ICommandHandler<S2_FailingInner>
    {
        public ValueTask<Result> Handle(S2_FailingInner request, CancellationToken cancellationToken)
        {
            EventLog.Add("S2-inner:throwing");
            throw new InvalidOperationException("Inner DB constraint violation");
        }
    }

    // Scenario 3: Inner succeeds, outer fails after
    [Transactional] public sealed record S3_Outer(string Data) : ICommand<Result>;
    [Transactional] public sealed record S3_InnerOk(string Data) : ICommand<Result>;

    public sealed class S3_OuterHandler(IMediator m) : ICommandHandler<S3_Outer>
    {
        public async ValueTask<Result> Handle(S3_Outer request, CancellationToken cancellationToken)
        {
            EventLog.Add("S3-outer:start");
            await m.Send(new S3_InnerOk("inner"), cancellationToken);
            EventLog.Add("S3-outer:inner-ok-then-fail");
            throw new InvalidOperationException("Outer failed after inner succeeded");
        }
    }
    public sealed class S3_InnerOkHandler : ICommandHandler<S3_InnerOk>
    {
        public ValueTask<Result> Handle(S3_InnerOk request, CancellationToken cancellationToken)
        {
            EventLog.Add("S3-inner:ok");
            return new(Result.Success());
        }
    }

    // Scenario 5: PostCommit after nested
    [Transactional] public sealed record S5_Outer(string Data) : ICommand<Result>;
    [Transactional] public sealed record S5_Inner(string Data) : ICommand<Result>;

    public sealed class S5_OuterHandler(IMediator m, IPostCommitTaskQueue q) : ICommandHandler<S5_Outer>
    {
        public async ValueTask<Result> Handle(S5_Outer request, CancellationToken cancellationToken)
        {
            EventLog.Add("S5-outer:start");
            q.Enqueue(async _ => { EventLog.Add("PostCommit:executed"); await Task.CompletedTask; });
            var r = await m.Send(new S5_Inner("inner"), cancellationToken);
            EventLog.Add("S5-outer:end");
            return r;
        }
    }
    public sealed class S5_InnerHandler : ICommandHandler<S5_Inner>
    {
        public ValueTask<Result> Handle(S5_Inner request, CancellationToken cancellationToken)
        {
            EventLog.Add("S5-inner:ok");
            return new(Result.Success());
        }
    }

    // --- Tracking UoW ---
    private sealed class TrackingUow : IUnitOfWork
    {
        private int _beginCount, _commitCount, _rollbackCount, _saveCount;
        public int BeginCount => _beginCount;
        public int CommitCount => _commitCount;
        public int RollbackCount => _rollbackCount;
        public int SaveCount => _saveCount;

        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _beginCount); EventLog.Add("UoW:Begin"); return ValueTask.CompletedTask; }
        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _saveCount); EventLog.Add("UoW:SaveChanges"); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _commitCount); EventLog.Add("UoW:Commit"); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _rollbackCount); EventLog.Add("UoW:Rollback"); return ValueTask.CompletedTask; }
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private (IMediator mediator, TrackingUow uow) Build()
    {
        var uow = new TrackingUow();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(NestedTransactionScenariosTests).Assembly));
        services.Configure<TransactionBehaviorOptions>(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddSingleton<IUnitOfWork>(uow);
        services.AddScoped<IPostCommitTaskQueue, PostCommitTaskQueue>();
        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), uow);
    }

    // =====================================================
    // SCENARIO 1: L1 → L2 → L3 all succeed — single transaction
    // =====================================================
    [Fact]
    public async Task S1_Three_Levels_Deep_All_Succeed_Single_Transaction()
    {
        ResetLog();
        var (mediator, uow) = Build();

        var result = await mediator.Send(new S1_Level1("test"));

        result.IsSuccess.Should().BeTrue();
        uow.BeginCount.Should().Be(1, "only outermost opens transaction");
        uow.CommitCount.Should().Be(1, "only outermost commits");
        uow.RollbackCount.Should().Be(0);
        uow.SaveCount.Should().Be(1);

        EventLog.Should().Equal(
            "UoW:Begin",
            "L1:start",
            "L2:start",
            "L3:executed",
            "L2:end",
            "L1:end",
            "UoW:SaveChanges",
            "UoW:Commit"
        );
    }

    // =====================================================
    // SCENARIO 2: Inner command fails — entire transaction rolls back
    // =====================================================
    [Fact]
    public async Task S2_Inner_Failure_Rolls_Back_Entire_Transaction()
    {
        ResetLog();
        var (mediator, uow) = Build();

        var act = () => mediator.Send(new S2_Outer("test")).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Inner DB constraint violation");

        uow.BeginCount.Should().Be(1);
        uow.CommitCount.Should().Be(0, "nothing committed");
        uow.RollbackCount.Should().Be(1, "everything rolled back");

        EventLog.Should().Equal(
            "UoW:Begin",
            "S2-outer:start",
            "S2-inner:throwing",
            "UoW:Rollback"
        );
    }

    // =====================================================
    // SCENARIO 3: Inner succeeds, outer fails — inner writes also rolled back
    // =====================================================
    [Fact]
    public async Task S3_Outer_Fails_After_Inner_Succeeds_Rolls_Back_Everything()
    {
        ResetLog();
        var (mediator, uow) = Build();

        var act = () => mediator.Send(new S3_Outer("test")).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Outer failed after inner succeeded");

        uow.BeginCount.Should().Be(1);
        uow.CommitCount.Should().Be(0, "nothing committed");
        uow.RollbackCount.Should().Be(1, "everything rolled back — including inner's writes");

        EventLog.Should().Contain("S3-inner:ok");
        EventLog.Should().Contain("S3-outer:inner-ok-then-fail");
        EventLog.Should().Contain("UoW:Rollback");
        EventLog.Should().NotContain("UoW:Commit");
    }

    // =====================================================
    // SCENARIO 4: Two sequential top-level calls — each gets own transaction
    // =====================================================
    [Fact]
    public async Task S4_Sequential_Calls_Get_Independent_Transactions()
    {
        ResetLog();
        var (mediator, uow) = Build();

        await mediator.Send(new S1_Level1("first"));
        await mediator.Send(new S1_Level1("second"));

        uow.BeginCount.Should().Be(2);
        uow.CommitCount.Should().Be(2);
        uow.RollbackCount.Should().Be(0);
    }

    // =====================================================
    // SCENARIO 5: PostCommit tasks execute only after outermost commit
    // =====================================================
    [Fact]
    public async Task S5_PostCommit_After_Outermost_Commit()
    {
        ResetLog();
        var (mediator, uow) = Build();

        await mediator.Send(new S5_Outer("test"));

        var commitIdx = EventLog.IndexOf("UoW:Commit");
        var postCommitIdx = EventLog.IndexOf("PostCommit:executed");

        commitIdx.Should().BeGreaterThanOrEqualTo(0);
        postCommitIdx.Should().BeGreaterThan(commitIdx,
            "post-commit tasks must execute AFTER commit");
    }
}
