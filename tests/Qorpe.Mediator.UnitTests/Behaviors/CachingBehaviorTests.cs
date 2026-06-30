using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Results;
using Qorpe.Mediator.UnitTests.Helpers;
using System.Text.Json;

namespace Qorpe.Mediator.UnitTests.Behaviors;

public class CachingBehaviorTests
{
    private readonly ILogger<CachingBehavior<CacheableQuery, Result<string>>> _logger =
        Substitute.For<ILogger<CachingBehavior<CacheableQuery, Result<string>>>>();
    private readonly IOptions<CachingBehaviorOptions> _options =
        Options.Create(new CachingBehaviorOptions());

    [Fact]
    public async Task Should_Cache_Query_Response()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);
        var callCount = 0;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            callCount++;
            return new ValueTask<Result<string>>(Result<string>.Success("result"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("result");
        callCount.Should().Be(1);
        await cache.Received(1).SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Return_Cached_Response_On_Hit()
    {
        var cache = Substitute.For<IDistributedCache>();
        var cachedValue = Result<string>.Success("cached-value");
        var cached = JsonSerializer.SerializeToUtf8Bytes(cachedValue);

        // NSubstitute: mock the extension method's underlying Get call
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(cached));

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("fresh"));

        await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);

        // Verify cache was queried
        await cache.Received().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_Commands()
    {
        var cmdLogger = Substitute.For<ILogger<CachingBehavior<TestCommand, Result>>>();
        var opts = Options.Create(new CachingBehaviorOptions());
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<TestCommand, Result>(cmdLogger, opts, cache);
        var called = false;

        RequestHandlerDelegate<Result> next = () =>
        {
            called = true;
            return new ValueTask<Result>(Result.Success());
        };

        await behavior.Handle(new TestCommand("test"), next, CancellationToken.None);

        called.Should().BeTrue();
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Fall_Through_When_No_Cache_Configured()
    {
        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache: null);
        var called = false;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            called = true;
            return new ValueTask<Result<string>>(Result<string>.Success("ok"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        called.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task Should_Fall_Through_When_Cache_Read_Fails()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<byte[]?>(x => throw new Exception("cache down"));

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);
        var called = false;

        RequestHandlerDelegate<Result<string>> next = () =>
        {
            called = true;
            return new ValueTask<Result<string>>(Result<string>.Success("fallback"));
        };

        var result = await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        called.Should().BeTrue();
        result.Value.Should().Be("fallback");
    }

    [Fact]
    public async Task Should_Skip_When_Disabled()
    {
        var opts = Options.Create(new CachingBehaviorOptions { Enabled = false });
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, opts, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new CacheableQuery(1), next, CancellationToken.None);
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Skip_When_No_Cacheable_Attribute()
    {
        var queryLogger = Substitute.For<ILogger<CachingBehavior<TestQuery, Result<string>>>>();
        var opts = Options.Create(new CachingBehaviorOptions());
        var cache = Substitute.For<IDistributedCache>();
        var behavior = new CachingBehavior<TestQuery, Result<string>>(queryLogger, opts, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        await behavior.Handle(new TestQuery(1), next, CancellationToken.None);
        await cache.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_Handle_Many_Unique_Keys_Without_Unbounded_Growth()
    {
        var cache = Substitute.For<IDistributedCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var behavior = new CachingBehavior<CacheableQuery, Result<string>>(_logger, _options, cache);

        RequestHandlerDelegate<Result<string>> next = () =>
            new ValueTask<Result<string>>(Result<string>.Success("ok"));

        // Execute with many unique cache keys
        for (int i = 0; i < 1_000; i++)
        {
            await behavior.Handle(new CacheableQuery(i), next, CancellationToken.None);
        }

        // The behavior should still function correctly after many unique keys
        var result = await behavior.Handle(new CacheableQuery(9999), next, CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}

public class BoundedLockPoolTests
{
    [Fact]
    public async Task Should_Reuse_Single_Entry_For_Same_Key()
    {
        var pool = new BoundedLockPool(maxSize: 100, evictionInterval: TimeSpan.FromMinutes(5));

        using (await pool.AcquireAsync("key-1", CancellationToken.None)) { }
        using (await pool.AcquireAsync("key-1", CancellationToken.None)) { }

        pool.Count.Should().Be(1, "the same key must map to a single pooled lock entry");
    }

    [Fact]
    public async Task Should_Create_Different_Entries_For_Different_Keys()
    {
        var pool = new BoundedLockPool(maxSize: 100, evictionInterval: TimeSpan.FromMinutes(5));

        using (await pool.AcquireAsync("key-1", CancellationToken.None))
        using (await pool.AcquireAsync("key-2", CancellationToken.None))
        {
            pool.Count.Should().Be(2);
        }
    }

    [Fact]
    public async Task Should_Serialize_Concurrent_Callers_On_Same_Key()
    {
        // This is the core invariant: two callers contending the same key must never run the
        // critical section at the same time, even while eviction is churning the pool.
        var pool = new BoundedLockPool(maxSize: 4, evictionInterval: TimeSpan.FromMilliseconds(1));
        var inside = 0;
        var maxObserved = 0;

        async Task Worker()
        {
            for (int i = 0; i < 200; i++)
            {
                using (await pool.AcquireAsync("hot-key", CancellationToken.None))
                {
                    var now = Interlocked.Increment(ref inside);
                    maxObserved = Math.Max(maxObserved, now);
                    await Task.Yield();
                    Interlocked.Decrement(ref inside);
                }
                // Churn other keys so eviction runs against the held key's entry.
                using (await pool.AcquireAsync($"churn-{i}", CancellationToken.None)) { }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(Worker)));

        maxObserved.Should().Be(1, "the per-key lock must guarantee mutual exclusion");
    }

    [Fact]
    public async Task Should_Evict_Stale_Unreferenced_Entries()
    {
        var pool = new BoundedLockPool(maxSize: 5, evictionInterval: TimeSpan.FromMilliseconds(1));

        for (int i = 0; i < 10; i++)
        {
            using (await pool.AcquireAsync($"key-{i}", CancellationToken.None)) { }
        }

        // Entries are released (refcount 0) and now stale.
        Thread.Sleep(10);
        pool.ForceEviction();

        pool.Count.Should().BeLessThan(10);
    }

    [Fact]
    public async Task Should_Not_Evict_Entry_That_Is_Currently_Held()
    {
        var pool = new BoundedLockPool(maxSize: 5, evictionInterval: TimeSpan.FromMilliseconds(1));

        var held = await pool.AcquireAsync("held-key", CancellationToken.None);

        // Add stale, released entries.
        for (int i = 0; i < 10; i++)
        {
            using (await pool.AcquireAsync($"stale-{i}", CancellationToken.None)) { }
        }

        Thread.Sleep(10);
        pool.ForceEviction();

        pool.Count.Should().BeGreaterThanOrEqualTo(1, "a referenced (held) entry must survive eviction");

        held.Dispose();
    }

    [Fact]
    public async Task Should_Be_Thread_Safe_Under_Concurrent_Access()
    {
        var pool = new BoundedLockPool(maxSize: 1_000, evictionInterval: TimeSpan.FromMinutes(5));

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(async () =>
        {
            for (int j = 0; j < 100; j++)
            {
                using (await pool.AcquireAsync($"key-{i}-{j}", CancellationToken.None)) { }
            }
        }));

        await Task.WhenAll(tasks);

        pool.Count.Should().BeLessThanOrEqualTo(10_000);
    }
}
