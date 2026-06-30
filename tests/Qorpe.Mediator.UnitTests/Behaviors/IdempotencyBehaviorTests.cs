using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class IdempotencyBehaviorTests
{
    private readonly ILogger<IdempotencyBehavior<IdempotentCommand, Result>> _logger =
        Substitute.For<ILogger<IdempotencyBehavior<IdempotentCommand, Result>>>();
    private readonly IOptions<IdempotencyBehaviorOptions> _options =
        Options.Create(new IdempotencyBehaviorOptions());

    [Fact]
    public async Task Should_Execute_First_Time_And_Cache()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        var result = await behavior.Handle(new IdempotentCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await store.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<Result>(),
            Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Return_Cached_On_Duplicate()
    {
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        store.GetAsync<Result>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store);
        var handlerCalled = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            handlerCalled = true;
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new IdempotentCommand("data"), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Should_Not_Store_Or_Remove_When_Handler_Fails()
    {
        // A handler failure must not store a result (nothing succeeded) and must NOT call
        // RemoveAsync — the previous implementation removed the key on ANY failure, which could
        // delete a previously stored successful result and defeat idempotency.
        var store = Substitute.For<IIdempotencyStore>();
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store);

        RequestHandlerDelegate<Result> next = () => throw new InvalidOperationException("fail");

        var act = async () => await behavior.Handle(new IdempotentCommand("data"), next, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await store.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<Result>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Queries()
    {
        var queryLogger = Substitute.For<ILogger<IdempotencyBehavior<TestQuery, Result<string>>>>();
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<TestQuery, Result<string>>(queryLogger, _options, store);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new TestQuery(1), next, CancellationToken.None);
        await store.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Execute_Normally_When_Store_Down()
    {
        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store: null);
        var called = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        var result = await behavior.Handle(new IdempotentCommand("data"), next, CancellationToken.None);
        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Skip_When_No_Idempotent_Attribute()
    {
        var cmdLogger = Substitute.For<ILogger<IdempotencyBehavior<TestCommand, Result>>>();
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<TestCommand, Result>(cmdLogger, _options, store);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);
        await store.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new IdempotencyBehaviorOptions { Enabled = false });
        var store = Substitute.For<IIdempotencyStore>();
        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, opts, store);

        RequestHandlerDelegate<Result> next = () => new ValueTask<Result>(Result.Success());

        await behavior.Handle(new IdempotentCommand("data"), next, CancellationToken.None);
        await store.DidNotReceive().ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Serialize_Concurrent_Requests_With_Same_Key()
    {
        var executionCount = 0;
        var store = new InMemoryIdempotencyStore();

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store);

        RequestHandlerDelegate<Result> next = async () =>
        {
            Interlocked.Increment(ref executionCount);
            await Task.Delay(50); // Simulate work
            return Result.Success();
        };

        var request = new IdempotentCommand("same-data");

        // Launch concurrent requests with the same idempotency key
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            behavior.Handle(request, next, CancellationToken.None).AsTask());

        await Task.WhenAll(tasks);

        // Only the first should have executed the handler; rest should get cached result
        executionCount.Should().Be(1, "per-key lock should serialize concurrent identical requests");
    }

    [Fact]
    public async Task Should_Allow_Parallel_Execution_For_Different_Keys()
    {
        var executionCount = 0;
        var store = new InMemoryIdempotencyStore();

        var behavior = new IdempotencyBehavior<IdempotentCommand, Result>(_logger, _options, store);

        RequestHandlerDelegate<Result> next = () =>
        {
            Interlocked.Increment(ref executionCount);
            return new ValueTask<Result>(Result.Success());
        };

        // Different keys should not block each other
        var tasks = Enumerable.Range(0, 10).Select(i =>
            behavior.Handle(new IdempotentCommand($"data-{i}"), next, CancellationToken.None).AsTask());

        await Task.WhenAll(tasks);

        executionCount.Should().Be(10, "different idempotency keys should execute independently");
    }
}

/// <summary>
/// Simple in-memory idempotency store for testing concurrent behavior.
/// </summary>
internal sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _store = new();

    public ValueTask<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => new(_store.ContainsKey(key));

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var value) && value is T typed)
        {
            return new(typed);
        }
        return new(default(T));
    }

    public ValueTask SetAsync<T>(string key, T value, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        _store[key] = value!;
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(key, out _);
        return ValueTask.CompletedTask;
    }
}
