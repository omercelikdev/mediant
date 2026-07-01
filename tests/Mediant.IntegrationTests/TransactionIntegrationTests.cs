using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.IntegrationTests;

/// <summary>
/// Advanced transaction integration tests covering:
/// - Nested command dispatch through IMediator
/// - Multi-entity operations with SaveChanges auto-flush
/// - PostCommitTaskQueue integration
/// - Rollback propagation across nested commands
/// - IUnitOfWork call sequence verification
/// </summary>
public class TransactionIntegrationTests
{
    // --- Shared tracking infrastructure ---
    private static readonly List<string> UowCallLog = new();
    private static readonly List<string> PostCommitLog = new();
    private static readonly List<string> HandlerLog = new();

    private static void ResetLogs()
    {
        UowCallLog.Clear();
        PostCommitLog.Clear();
        HandlerLog.Clear();
    }

    // --- Commands ---
    [Transactional]
    public sealed record ParentCommand(string Data) : ICommand<Result>;

    [Transactional]
    public sealed record ChildCommand(string Data) : ICommand<Result>;

    [Transactional]
    public sealed record FailingChildCommand(string Data) : ICommand<Result>;

    [Transactional]
    public sealed record MultiSaveCommand(string Data) : ICommand<Result>;

    [Transactional]
    public sealed record PostCommitIntegrationCommand(string Data) : ICommand<Result>;

    [Transactional]
    public sealed record FailingParentCommand(string Data) : ICommand<Result>;

    // --- Handlers ---
    public sealed class ParentCommandHandler(IMediator mediator) : ICommandHandler<ParentCommand>
    {
        public async ValueTask<Result> Handle(ParentCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("Parent:Before");
            var result = await mediator.Send(new ChildCommand("child-of-" + request.Data), cancellationToken);
            HandlerLog.Add("Parent:After");
            return result;
        }
    }

    public sealed class ChildCommandHandler : ICommandHandler<ChildCommand>
    {
        public ValueTask<Result> Handle(ChildCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("Child:" + request.Data);
            return new(Result.Success());
        }
    }

    public sealed class FailingChildCommandHandler : ICommandHandler<FailingChildCommand>
    {
        public ValueTask<Result> Handle(FailingChildCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("FailingChild:throwing");
            throw new InvalidOperationException("Child handler failed");
        }
    }

    public sealed class MultiSaveCommandHandler : ICommandHandler<MultiSaveCommand>
    {
        public async ValueTask<Result> Handle(MultiSaveCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("MultiSave:entity1");
            // Simulate adding entity 1 — no explicit SaveChanges call
            HandlerLog.Add("MultiSave:entity2");
            // Simulate adding entity 2 — no explicit SaveChanges call
            // TransactionBehavior should auto-flush via SaveChangesAsync before commit
            await Task.CompletedTask;
            return Result.Success();
        }
    }

    public sealed class PostCommitIntegrationCommandHandler(IPostCommitTaskQueue queue) : ICommandHandler<PostCommitIntegrationCommand>
    {
        public ValueTask<Result> Handle(PostCommitIntegrationCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("PostCommitHandler:executing");
            queue.Enqueue(async _ =>
            {
                PostCommitLog.Add("Task1:executed");
                await Task.CompletedTask;
            });
            queue.Enqueue(async _ =>
            {
                PostCommitLog.Add("Task2:executed");
                await Task.CompletedTask;
            });
            return new(Result.Success());
        }
    }

    // --- Tracking UnitOfWork ---
    public sealed class TrackingUnitOfWork : IUnitOfWork
    {
        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) { UowCallLog.Add("Begin"); return ValueTask.CompletedTask; }
        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) { UowCallLog.Add("SaveChanges"); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken cancellationToken) { UowCallLog.Add("Commit"); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) { UowCallLog.Add("Rollback"); return ValueTask.CompletedTask; }
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(TransactionIntegrationTests).Assembly));
        services.Configure<TransactionBehaviorOptions>(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        var trackingUow = new TrackingUnitOfWork();
        services.AddSingleton<IUnitOfWork>(trackingUow);
        services.AddSingleton(trackingUow);
        services.AddScoped<IPostCommitTaskQueue, PostCommitTaskQueue>();
        services.AddSingleton<IAuditStore>(NullAuditStore.Instance);

        return services.BuildServiceProvider();
    }

    // --- Tests ---

    [Fact]
    public async Task Nested_Command_Via_Mediator_Uses_Single_Transaction()
    {
        ResetLogs();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ParentCommand("test"));

        result.IsSuccess.Should().BeTrue();
        HandlerLog.Should().Equal("Parent:Before", "Child:child-of-test", "Parent:After");
        UowCallLog.Should().Equal("Begin", "SaveChanges", "Commit");
    }

    [Fact]
    public async Task Nested_Command_Failure_Rolls_Back_Entire_Transaction()
    {
        ResetLogs();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var act = () => mediator.Send(new FailingParentCommand("test")).AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Child handler failed");

        UowCallLog.Should().Equal("Begin", "Rollback");
    }

    [Fact]
    public async Task SaveChanges_Called_Before_Commit_For_Multi_Entity_Operations()
    {
        ResetLogs();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new MultiSaveCommand("multi"));

        result.IsSuccess.Should().BeTrue();
        HandlerLog.Should().Equal("MultiSave:entity1", "MultiSave:entity2");
        // SaveChanges MUST come before Commit
        var saveIdx = UowCallLog.IndexOf("SaveChanges");
        var commitIdx = UowCallLog.IndexOf("Commit");
        saveIdx.Should().BeGreaterThanOrEqualTo(0);
        commitIdx.Should().BeGreaterThan(saveIdx);
    }

    [Fact]
    public async Task PostCommitTasks_Execute_After_Transaction_Commit()
    {
        ResetLogs();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        var result = await mediator.Send(new PostCommitIntegrationCommand("test"));

        result.IsSuccess.Should().BeTrue();
        UowCallLog.Should().Contain("Commit");
        PostCommitLog.Should().Equal("Task1:executed", "Task2:executed");

        // Verify commit happened before post-commit tasks
        var commitIdx = UowCallLog.IndexOf("Commit");
        commitIdx.Should().BeGreaterThanOrEqualTo(0, "transaction should have committed");
    }

    [Fact]
    public async Task Sequential_Independent_Commands_Get_Separate_Transactions()
    {
        ResetLogs();
        using var sp = BuildProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Send(new ChildCommand("first"));
        await mediator.Send(new ChildCommand("second"));

        // Each command should get its own transaction
        UowCallLog.Where(c => c == "Begin").Should().HaveCount(2);
        UowCallLog.Where(c => c == "Commit").Should().HaveCount(2);
    }

    public sealed class FailingParentCommandHandler(IMediator mediator) : ICommandHandler<FailingParentCommand>
    {
        public async ValueTask<Result> Handle(FailingParentCommand request, CancellationToken cancellationToken)
        {
            HandlerLog.Add("FailingParent:Before");
            await mediator.Send(new FailingChildCommand("will-fail"), cancellationToken);
            HandlerLog.Add("FailingParent:After"); // Should not reach here
            return Result.Success();
        }
    }
}
