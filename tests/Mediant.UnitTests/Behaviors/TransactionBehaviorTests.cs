using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.DependencyInjection;
using Mediant.Results;
using Mediant.UnitTests.Helpers;

namespace Mediant.UnitTests.Behaviors;

public class TransactionBehaviorTests
{
    private readonly ILogger<TransactionBehavior<TransactionalCommand, Result>> _logger =
        Substitute.For<ILogger<TransactionBehavior<TransactionalCommand, Result>>>();
    private readonly IOptions<TransactionBehaviorOptions> _options =
        Options.Create(new TransactionBehaviorOptions());

    [Fact]
    public async Task Should_Begin_Commit_On_Success()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await uow.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Rollback_On_Exception()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await uow.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Clear_PostCommit_Queue_On_Rollback()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var queue = new PostCommitTaskQueue(Substitute.For<ILogger<PostCommitTaskQueue>>());
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow, queue);

        var sideEffectRan = false;
        RequestHandlerDelegate<Result> next = () =>
        {
            // A handler enqueues post-commit work, then the transaction fails.
            queue.Enqueue(_ => { sideEffectRan = true; return Task.CompletedTask; });
            throw new InvalidOperationException("fail");
        };

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // The queue must have been cleared on rollback so the side effect never fires —
        // including for any later committing command in the same scope.
        await queue.ExecuteAsync(CancellationToken.None);
        sideEffectRan.Should().BeFalse("post-commit tasks from a rolled-back transaction must be discarded");
    }

    [Fact]
    public async Task Should_Skip_Queries()
    {
        var queryLogger = Substitute.For<ILogger<TransactionBehavior<TestQuery, Result<string>>>>();
        var queryOpts = Options.Create(new TransactionBehaviorOptions());
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TestQuery, Result<string>>(queryLogger, queryOpts, uow);

        RequestHandlerDelegate<Result<string>> next = () => new ValueTask<Result<string>>(Result<string>.Success("ok"));

        var result = await behavior.Handle(new TestQuery(1), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Throw_When_No_UnitOfWork()
    {
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, unitOfWork: null);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IUnitOfWork is required*");
    }

    [Fact]
    public async Task Should_Log_Critical_When_Rollback_Fails()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("rollback failed"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("original error");

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        // Original exception should be preserved even when rollback fails
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("original error");
    }

    [Fact]
    public async Task Should_Call_SaveChangesAsync_Before_Commit()
    {
        var callOrder = new List<string>();
        var uow = new SaveChangesTrackingUow(callOrder);
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        callOrder.Should().Equal("Begin", "SaveChanges", "Commit");
    }

    [Fact]
    public async Task Should_Not_Call_SaveChanges_On_Handler_Failure()
    {
        var callOrder = new List<string>();
        var uow = new SaveChangesTrackingUow(callOrder);
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");
        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        callOrder.Should().Equal("Begin", "Rollback");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new TransactionBehaviorOptions { Enabled = false });
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, opts, uow);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());
        var result = await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await uow.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Rollback_On_Commit_Failure_Not_Handler_Failure()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("commit constraint violation"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        var handlerCalled = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            handlerCalled = true;
            return new ValueTask<Result>(Result.Success());
        };

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit*");
        handlerCalled.Should().BeTrue("handler should have succeeded before commit failed");
        await uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Preserve_Original_Exception_When_Rollback_Fails_After_Commit_Failure()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new InvalidOperationException("commit failed"));
        uow.RollbackAsync(Arg.Any<CancellationToken>())
            .Returns(x => throw new Exception("rollback also failed"));

        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var act = async () => await behavior.Handle(new TransactionalCommand("data"), next, CancellationToken.None);

        // Commit exception should be preserved, not the rollback one
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*commit*");
    }

    // --- Nested Transaction Tests ---

    [Fact]
    public async Task Nested_Transaction_Should_Not_Call_BeginTransaction_Twice()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var outerBehavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        // Outer behavior starts transaction, inner "nested" call should detect and skip
        RequestHandlerDelegate<Result> next = async () =>
        {
            // Simulate nested command dispatch — another TransactionBehavior runs inside
            var innerBehavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
            return await innerBehavior.Handle(
                new TransactionalCommand("inner"),
                () => new ValueTask<Result>(Result.Success()),
                CancellationToken.None);
        };

        var result = await outerBehavior.Handle(new TransactionalCommand("outer"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // BeginTransaction should be called exactly ONCE — by the outer behavior only
        await uow.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nested_Transaction_Inner_Failure_Should_Rollback_Outer()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var outerBehavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        RequestHandlerDelegate<Result> next = async () =>
        {
            var innerBehavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);
            return await innerBehavior.Handle(
                new TransactionalCommand("inner"),
                () => throw new InvalidOperationException("inner failed"),
                CancellationToken.None);
        };

        var act = async () => await outerBehavior.Handle(
            new TransactionalCommand("outer"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("inner failed");
        await uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task After_Nested_Transaction_Completes_Flag_Should_Reset()
    {
        var uow = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TransactionalCommand, Result>(_logger, _options, uow);

        // First call — starts and commits transaction
        await behavior.Handle(
            new TransactionalCommand("first"),
            () => new ValueTask<Result>(Result.Success()),
            CancellationToken.None);

        // Second call — should start NEW transaction, not think it's nested
        await behavior.Handle(
            new TransactionalCommand("second"),
            () => new ValueTask<Result>(Result.Success()),
            CancellationToken.None);

        await uow.Received(2).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await uow.Received(2).CommitAsync(Arg.Any<CancellationToken>());
    }

    // --- Full Integration: Nested via IMediator ---

    [Transactional]
    public sealed record OuterCommand(string Data) : ICommand<Result>;
    [Transactional]
    public sealed record InnerCommand(string Data) : ICommand<Result>;

    public sealed class OuterCommandHandler(IMediator mediator) : ICommandHandler<OuterCommand>
    {
        public async ValueTask<Result> Handle(OuterCommand request, CancellationToken cancellationToken)
        {
            return await mediator.Send(new InnerCommand("nested-" + request.Data), cancellationToken);
        }
    }

    public sealed class InnerCommandHandler : ICommandHandler<InnerCommand>
    {
        public static int CallCount;
        public ValueTask<Result> Handle(InnerCommand request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            return new ValueTask<Result>(Result.Success());
        }
    }

    public sealed class TrackingUnitOfWork : IUnitOfWork
    {
        private int _beginCount;
        private int _commitCount;
        private int _rollbackCount;
        public int BeginCount => _beginCount;
        public int CommitCount => _commitCount;
        public int RollbackCount => _rollbackCount;

        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _beginCount); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _commitCount); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref _rollbackCount); return ValueTask.CompletedTask; }
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Integration_Nested_Command_Via_Mediator_Should_Use_Single_Transaction()
    {
        InnerCommandHandler.CallCount = 0;
        var trackingUow = new TrackingUnitOfWork();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(TransactionBehaviorTests).Assembly));
        services.AddSingleton<IUnitOfWork>(trackingUow);
        services.Configure<TransactionBehaviorOptions>(_ => { });
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        var result = await mediator.Send(new OuterCommand("test"));

        result.IsSuccess.Should().BeTrue();
        InnerCommandHandler.CallCount.Should().Be(1);
        trackingUow.BeginCount.Should().Be(1, "only outer transaction should call BeginTransaction");
        trackingUow.CommitCount.Should().Be(1, "only outer transaction should commit");
    }

    // --- Test helpers ---
    private sealed class SaveChangesTrackingUow(List<string> callOrder) : IUnitOfWork
    {
        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) { callOrder.Add("Begin"); return ValueTask.CompletedTask; }
        public ValueTask CommitAsync(CancellationToken cancellationToken) { callOrder.Add("Commit"); return ValueTask.CompletedTask; }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) { callOrder.Add("Rollback"); return ValueTask.CompletedTask; }
        public ValueTask SaveChangesAsync(CancellationToken cancellationToken) { callOrder.Add("SaveChanges"); return ValueTask.CompletedTask; }
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
