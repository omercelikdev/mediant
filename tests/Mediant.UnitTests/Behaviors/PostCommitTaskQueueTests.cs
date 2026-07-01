using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Configuration;
using Mediant.Results;
using Mediant.Behaviors.Attributes;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Behaviors;

public class PostCommitTaskQueueTests
{
    // --- Test types ---
    [Transactional]
    public sealed record PostCommitCommand(string Data) : ICommand<Result>;
    public sealed class PostCommitCommandHandler : ICommandHandler<PostCommitCommand>
    {
        private readonly IPostCommitTaskQueue _queue;
        public PostCommitCommandHandler(IPostCommitTaskQueue queue) => _queue = queue;

        public ValueTask<Result> Handle(PostCommitCommand request, CancellationToken cancellationToken)
        {
            _queue.Enqueue(async ct => { await Task.Delay(1, ct); });
            return new(Result.Success());
        }
    }

    // --- PostCommitTaskQueue unit tests ---

    [Fact]
    public async Task ExecuteAsync_With_No_Tasks_Should_Complete_Immediately()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        await queue.ExecuteAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Execute_All_Enqueued_Tasks()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var executed = new List<int>();

        queue.Enqueue(async _ => { executed.Add(1); await Task.CompletedTask; });
        queue.Enqueue(async _ => { executed.Add(2); await Task.CompletedTask; });
        queue.Enqueue(async _ => { executed.Add(3); await Task.CompletedTask; });

        await queue.ExecuteAsync(CancellationToken.None);

        executed.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Execute_Tasks_Sequentially()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var order = new List<int>();

        queue.Enqueue(async ct => { await Task.Delay(50, ct); order.Add(1); });
        queue.Enqueue(async ct => { await Task.Delay(10, ct); order.Add(2); });
        queue.Enqueue(async _ => { order.Add(3); await Task.CompletedTask; });

        await queue.ExecuteAsync(CancellationToken.None);

        // Sequential execution means 1 finishes before 2 starts
        order.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Clear_Queue_After_Execution()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var count = 0;

        queue.Enqueue(async _ => { count++; await Task.CompletedTask; });
        await queue.ExecuteAsync(CancellationToken.None);
        count.Should().Be(1);

        // Second execution should do nothing — queue was cleared
        await queue.ExecuteAsync(CancellationToken.None);
        count.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Not_Throw_On_Task_Failure()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var executed = new List<int>();

        queue.Enqueue(async _ => { executed.Add(1); await Task.CompletedTask; });
        queue.Enqueue(_ => throw new InvalidOperationException("Task 2 failed"));
        queue.Enqueue(async _ => { executed.Add(3); await Task.CompletedTask; });

        // Should not throw — failures are logged but swallowed
        await queue.ExecuteAsync(CancellationToken.None);

        // Task 1 and 3 should still execute, task 2 failed silently
        executed.Should().Equal(1, 3);
    }

    [Fact]
    public void Enqueue_Null_Should_Throw_ArgumentNullException()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var act = () => queue.Enqueue(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // --- TransactionBehavior integration with PostCommitTaskQueue ---

    [Fact]
    public async Task TransactionBehavior_Should_Execute_PostCommitQueue_After_Commit()
    {
        var taskExecuted = false;
        var commitOccurred = false;
        var taskExecutedAfterCommit = false;

        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        queue.Enqueue(async _ =>
        {
            taskExecutedAfterCommit = commitOccurred;
            taskExecuted = true;
            await Task.CompletedTask;
        });

        var unitOfWork = new TestUnitOfWork(onCommit: () => commitOccurred = true);
        var behavior = new TransactionBehavior<PostCommitCommand, Result>(
            NullLogger<TransactionBehavior<PostCommitCommand, Result>>.Instance,
            Options.Create(new TransactionBehaviorOptions()),
            unitOfWork,
            queue);

        var result = await behavior.Handle(
            new PostCommitCommand("test"),
            () => new ValueTask<Result>(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        taskExecuted.Should().BeTrue("post-commit task should have been executed");
        taskExecutedAfterCommit.Should().BeTrue("post-commit task should run AFTER commit");
    }

    [Fact]
    public async Task TransactionBehavior_Should_Not_Execute_PostCommitQueue_On_Rollback()
    {
        var taskExecuted = false;
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        queue.Enqueue(async _ => { taskExecuted = true; await Task.CompletedTask; });

        var unitOfWork = new TestUnitOfWork();
        var behavior = new TransactionBehavior<PostCommitCommand, Result>(
            NullLogger<TransactionBehavior<PostCommitCommand, Result>>.Instance,
            Options.Create(new TransactionBehaviorOptions()),
            unitOfWork,
            queue);

        var act = () => behavior.Handle(
            new PostCommitCommand("test"),
            () => throw new InvalidOperationException("Handler failed"),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>();
        taskExecuted.Should().BeFalse("post-commit tasks should NOT execute on rollback");
    }

    [Fact]
    public async Task TransactionBehavior_Without_PostCommitQueue_Should_Work_Normally()
    {
        var unitOfWork = new TestUnitOfWork();
        var behavior = new TransactionBehavior<PostCommitCommand, Result>(
            NullLogger<TransactionBehavior<PostCommitCommand, Result>>.Instance,
            Options.Create(new TransactionBehaviorOptions()),
            unitOfWork,
            postCommitQueue: null);

        var result = await behavior.Handle(
            new PostCommitCommand("test"),
            () => new ValueTask<Result>(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        unitOfWork.CommitCalled.Should().BeTrue();
    }

    [Fact]
    public async Task PostCommitQueue_Concurrent_Enqueue_Should_Be_Safe()
    {
        var queue = new PostCommitTaskQueue(NullLogger<PostCommitTaskQueue>.Instance);
        var count = 0;

        // Enqueue from multiple threads
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            queue.Enqueue(async _ => { Interlocked.Increment(ref count); await Task.CompletedTask; });
        }));

        await Task.WhenAll(tasks);
        await queue.ExecuteAsync(CancellationToken.None);

        count.Should().Be(100);
    }

    // --- Test helpers ---
    private sealed class TestUnitOfWork : IUnitOfWork
    {
        private readonly Action? _onCommit;
        public bool CommitCalled { get; private set; }

        public TestUnitOfWork(Action? onCommit = null) => _onCommit = onCommit;

        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            CommitCalled = true;
            _onCommit?.Invoke();
            return ValueTask.CompletedTask;
        }
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
