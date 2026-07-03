using System.Collections.Concurrent;
using Mediant.Abstractions;
using Mediant.Behaviors.Idempotency;

namespace Mediant.UnitTests.Idempotency;

public class IdempotentOperationCoordinatorTests
{
    [Fact]
    public async Task New_Operation_Completes_And_Replays_On_Next_Begin()
    {
        var coordinator = NewCoordinator();

        using (var op = await coordinator.BeginAsync<string>("key-1"))
        {
            op.Status.Should().Be(IdempotentOperationStatus.New);
            await op.CompleteAsync("response-1", TimeSpan.FromMinutes(5));
        }

        using var replay = await coordinator.BeginAsync<string>("key-1");
        replay.Status.Should().Be(IdempotentOperationStatus.Replay);
        replay.StoredResponse.Should().Be("response-1");
    }

    [Fact]
    public async Task Dispose_Without_Complete_Stores_Nothing()
    {
        var coordinator = NewCoordinator();

        using (var op = await coordinator.BeginAsync<string>("key-fail"))
        {
            op.Status.Should().Be(IdempotentOperationStatus.New);
            // Operation failed — dispose without completing.
        }

        using var retry = await coordinator.BeginAsync<string>("key-fail");
        retry.Status.Should().Be(IdempotentOperationStatus.New, "a failed attempt must leave no entry so a retry can run");
    }

    [Fact]
    public async Task Matching_Fingerprint_Replays()
    {
        var coordinator = NewCoordinator();

        using (var op = await coordinator.BeginAsync<string>("key-fp", fingerprint: "abc"))
        {
            await op.CompleteAsync("stored", TimeSpan.FromMinutes(5));
        }

        using var replay = await coordinator.BeginAsync<string>("key-fp", fingerprint: "abc");
        replay.Status.Should().Be(IdempotentOperationStatus.Replay);
        replay.StoredResponse.Should().Be("stored");
    }

    [Fact]
    public async Task Different_Fingerprint_Is_Reported_As_Mismatch()
    {
        var coordinator = NewCoordinator();

        using (var op = await coordinator.BeginAsync<string>("key-fp2", fingerprint: "abc"))
        {
            await op.CompleteAsync("stored", TimeSpan.FromMinutes(5));
        }

        using var mismatch = await coordinator.BeginAsync<string>("key-fp2", fingerprint: "OTHER");
        mismatch.Status.Should().Be(IdempotentOperationStatus.FingerprintMismatch,
            "same key with a different payload must not silently replay");
    }

    [Fact]
    public async Task Missing_Fingerprint_On_Either_Side_Replays()
    {
        var coordinator = NewCoordinator();

        // Stored without a fingerprint, requested with one — verification is opt-in, so replay.
        using (var op = await coordinator.BeginAsync<string>("key-fp3"))
        {
            await op.CompleteAsync("stored", TimeSpan.FromMinutes(5));
        }

        using var replay = await coordinator.BeginAsync<string>("key-fp3", fingerprint: "abc");
        replay.Status.Should().Be(IdempotentOperationStatus.Replay);
    }

    [Fact]
    public async Task Zero_Timeout_Reports_InFlight_While_Key_Is_Locked()
    {
        var coordinator = NewCoordinator();

        using var first = await coordinator.BeginAsync<string>("key-busy");
        first.Status.Should().Be(IdempotentOperationStatus.New);

        using var second = await coordinator.BeginAsync<string>("key-busy", lockWaitTimeout: TimeSpan.Zero);
        second.Status.Should().Be(IdempotentOperationStatus.InFlight);
        second.StoredResponse.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_Same_Key_Callers_Are_Serialized_And_Second_Replays()
    {
        var coordinator = NewCoordinator();
        var executions = 0;

        async Task<string> RunOnce()
        {
            using var op = await coordinator.BeginAsync<string>("key-concurrent");
            if (op.Status == IdempotentOperationStatus.Replay)
            {
                return op.StoredResponse!;
            }

            Interlocked.Increment(ref executions);
            await Task.Delay(25);
            await op.CompleteAsync("only-once", TimeSpan.FromMinutes(5));
            return "only-once";
        }

        var results = await Task.WhenAll(RunOnce(), RunOnce(), RunOnce());

        executions.Should().Be(1, "concurrent same-key callers must serialize and replay the first result");
        results.Should().AllBe("only-once");
    }

    [Fact]
    public async Task Complete_Is_Rejected_For_Replay_And_After_Dispose()
    {
        var coordinator = NewCoordinator();

        using (var op = await coordinator.BeginAsync<string>("key-guard"))
        {
            await op.CompleteAsync("stored", TimeSpan.FromMinutes(5));
        }

        using var replay = await coordinator.BeginAsync<string>("key-guard");
        var completeReplay = () => replay.CompleteAsync("other", TimeSpan.FromMinutes(5)).AsTask();
        await completeReplay.Should().ThrowAsync<InvalidOperationException>();

        var disposed = await coordinator.BeginAsync<string>("key-guard2");
        disposed.Dispose();
        var completeDisposed = () => disposed.CompleteAsync("late", TimeSpan.FromMinutes(5)).AsTask();
        await completeDisposed.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Missing_Store_Throws_With_Clear_Message()
    {
        var coordinator = new DefaultIdempotentOperationCoordinator(store: null);

        var act = () => coordinator.BeginAsync<string>("key").AsTask();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IIdempotencyStore*");
    }

    private static DefaultIdempotentOperationCoordinator NewCoordinator()
        => new(new InMemoryIdempotencyStore());

    /// <summary>Minimal in-memory IIdempotencyStore for coordinator tests.</summary>
    private sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, object?> _entries = new(StringComparer.Ordinal);

        public ValueTask<bool> ExistsAsync(string idempotencyKey, CancellationToken cancellationToken)
            => new(_entries.ContainsKey(idempotencyKey));

        public ValueTask<TResponse?> GetAsync<TResponse>(string idempotencyKey, CancellationToken cancellationToken)
            => new(_entries.TryGetValue(idempotencyKey, out var value) && value is TResponse typed ? typed : default(TResponse?));

        public ValueTask SetAsync<TResponse>(string idempotencyKey, TResponse response, TimeSpan window, CancellationToken cancellationToken)
        {
            _entries[idempotencyKey] = response;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            _entries.TryRemove(idempotencyKey, out _);
            return ValueTask.CompletedTask;
        }
    }
}
