using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

/// <summary>
/// Opt-in nested savepoint semantics (#141): with
/// <see cref="TransactionBehaviorOptions.NestedSavepoints"/> enabled, a nested
/// <c>[Transactional]</c> command gets a savepoint; on failure only its work is unwound and the
/// outer transaction can still commit. With the default (off), the pre-existing join semantics
/// are byte-for-byte unchanged — no savepoint calls at all.
/// </summary>
public class NestedSavepointTests
{
    // Outer catches the inner failure and commits anyway.
    [Transactional] public sealed record SP_Outer(bool SwallowInnerFailure) : ICommand<Result>;
    [Transactional] public sealed record SP_FailingInner : ICommand<Result>;
    [Transactional] public sealed record SP_HealthyInner : ICommand<Result>;

    public sealed class SP_OuterHandler(IMediator m) : ICommandHandler<SP_Outer>
    {
        public async ValueTask<Result> Handle(SP_Outer request, CancellationToken cancellationToken)
        {
            try
            {
                await m.Send(new SP_FailingInner(), cancellationToken);
            }
            catch (InvalidOperationException) when (request.SwallowInnerFailure)
            {
                // Business decision: inner step is optional, keep going.
            }

            await m.Send(new SP_HealthyInner(), cancellationToken);
            return Result.Success();
        }
    }

    public sealed class SP_FailingInnerHandler : ICommandHandler<SP_FailingInner>
    {
        public ValueTask<Result> Handle(SP_FailingInner request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("inner failed");
    }

    public sealed class SP_HealthyInnerHandler : ICommandHandler<SP_HealthyInner>
    {
        public ValueTask<Result> Handle(SP_HealthyInner request, CancellationToken cancellationToken)
            => new(Result.Success());
    }

    private sealed class SavepointRecordingUow : IUnitOfWork
    {
        public List<string> Calls { get; } = new();

        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) { Calls.Add("Begin"); return ValueTask.CompletedTask; }
        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) { Calls.Add("Save"); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken cancellationToken) { Calls.Add("Commit"); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) { Calls.Add("Rollback"); return ValueTask.CompletedTask; }
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) { Calls.Add($"Savepoint:{name}"); return ValueTask.CompletedTask; }
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) { Calls.Add($"RollbackTo:{name}"); return ValueTask.CompletedTask; }
    }

    private static (IMediator mediator, SavepointRecordingUow uow) Build(bool nestedSavepoints)
    {
        var uow = new SavepointRecordingUow();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(NestedSavepointTests).Assembly));
        services.Configure<TransactionBehaviorOptions>(o => o.NestedSavepoints = nestedSavepoints);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddSingleton<IUnitOfWork>(uow);
        return (services.BuildServiceProvider().GetRequiredService<IMediator>(), uow);
    }

    [Fact]
    public async Task Enabled_Inner_Failure_Rolls_Back_To_Its_Savepoint_And_Outer_Commits()
    {
        var (mediator, uow) = Build(nestedSavepoints: true);

        var result = await mediator.Send(new SP_Outer(SwallowInnerFailure: true));

        result.IsSuccess.Should().BeTrue();
        uow.Calls.Should().Contain("Savepoint:mediant_sp_1");
        uow.Calls.Should().Contain("RollbackTo:mediant_sp_1");
        uow.Calls.Should().Contain("Commit");
        uow.Calls.Should().NotContain("Rollback", "only the savepoint is unwound, not the transaction");
        uow.Calls.Should().ContainSingle(c => c == "Begin");

        // The healthy inner after the failure reuses the same depth — its savepoint moves the
        // name to the new position, which is exactly the window it needs.
        uow.Calls.Count(c => c == "Savepoint:mediant_sp_1").Should().Be(2);
    }

    [Fact]
    public async Task Enabled_Uncaught_Inner_Failure_Still_Rolls_Back_The_Whole_Transaction()
    {
        var (mediator, uow) = Build(nestedSavepoints: true);

        await mediator.Invoking(m => m.Send(new SP_Outer(SwallowInnerFailure: false)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        uow.Calls.Should().Contain("RollbackTo:mediant_sp_1", "the nested behavior unwinds first");
        uow.Calls.Should().Contain("Rollback", "then the owner rolls back the whole transaction");
        uow.Calls.Should().NotContain("Commit");
    }

    [Fact]
    public async Task Disabled_Default_Makes_No_Savepoint_Calls_At_All()
    {
        var (mediator, uow) = Build(nestedSavepoints: false);

        var result = await mediator.Send(new SP_Outer(SwallowInnerFailure: true));

        result.IsSuccess.Should().BeTrue();
        uow.Calls.Should().NotContain(c => c.StartsWith("Savepoint:") || c.StartsWith("RollbackTo:"),
            "as-is join semantics: nested dispatch never touches the savepoint API by default");
        uow.Calls.Should().Equal("Begin", "Save", "Commit");
    }

    // Deeper nesting: L1 → L2 → L3, L3 fails, L2 swallows — savepoint names must not collide.
    [Transactional] public sealed record SPD_L1 : ICommand<Result>;
    [Transactional] public sealed record SPD_L2 : ICommand<Result>;
    [Transactional] public sealed record SPD_L3 : ICommand<Result>;

    public sealed class SPD_L1Handler(IMediator m) : ICommandHandler<SPD_L1>
    {
        public async ValueTask<Result> Handle(SPD_L1 request, CancellationToken cancellationToken)
            => await m.Send(new SPD_L2(), cancellationToken);
    }

    public sealed class SPD_L2Handler(IMediator m) : ICommandHandler<SPD_L2>
    {
        public async ValueTask<Result> Handle(SPD_L2 request, CancellationToken cancellationToken)
        {
            try { await m.Send(new SPD_L3(), cancellationToken); }
            catch (InvalidOperationException) { /* optional step */ }
            return Result.Success();
        }
    }

    public sealed class SPD_L3Handler : ICommandHandler<SPD_L3>
    {
        public ValueTask<Result> Handle(SPD_L3 request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("L3 failed");
    }

    [Fact]
    public async Task Enabled_Deeper_Nesting_Gets_Unique_Savepoint_Names_Per_Depth()
    {
        var (mediator, uow) = Build(nestedSavepoints: true);

        var result = await mediator.Send(new SPD_L1());

        result.IsSuccess.Should().BeTrue();
        uow.Calls.Should().Contain("Savepoint:mediant_sp_1", "L2 is one level below the owner");
        uow.Calls.Should().Contain("Savepoint:mediant_sp_2", "L3 is two levels below the owner");
        uow.Calls.Should().Contain("RollbackTo:mediant_sp_2", "only L3's window is unwound");
        uow.Calls.Should().NotContain("RollbackTo:mediant_sp_1");
        uow.Calls.Should().Contain("Commit");
    }
}
