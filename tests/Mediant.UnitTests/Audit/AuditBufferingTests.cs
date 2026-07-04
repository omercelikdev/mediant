using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.Audit;
using Mediant.Behaviors.DependencyInjection;

namespace Mediant.UnitTests.Audit;

public class AuditBufferingTests
{
    private static AuditEntry NewEntry(string type = "Cmd") => new() { RequestType = type, IsSuccess = true };

    private static (BufferedAuditStore<CountingAuditStore> Buffered, CountingAuditStore Inner) NewBufferedStore(
        int batchSize = 100, int capacity = 10_000)
    {
        var options = Options.Create(new AuditBufferOptions { BatchSize = batchSize, Capacity = capacity });
        var inner = new CountingAuditStore();
        var buffer = new AuditBuffer(options);
        return (new BufferedAuditStore<CountingAuditStore>(buffer, inner, options), inner);
    }

    [Fact]
    public async Task Flush_Writes_Buffered_Entries_In_Batches()
    {
        var (buffered, inner) = NewBufferedStore(batchSize: 100);

        for (int i = 0; i < 1000; i++)
        {
            await buffered.SaveAsync(NewEntry(), default);
        }

        inner.BatchCalls.Should().Be(0, "nothing is written before a flush");

        var written = await buffered.FlushAsync();

        written.Should().Be(1000);
        inner.Saved.Count.Should().Be(1000);
        inner.BatchCalls.Should().Be(10, "1000 entries at BatchSize=100 must take exactly 10 round-trips instead of 1000");
    }

    [Fact]
    public async Task Query_Flushes_First_So_Reads_Observe_Prior_Writes()
    {
        var (buffered, inner) = NewBufferedStore();

        await buffered.SaveAsync(NewEntry("CreateOrder"), default);

        var results = await buffered.QueryAsync(new AuditQuery { RequestType = "CreateOrder" }, default);

        results.Should().ContainSingle();
        inner.Saved.Should().ContainSingle();
    }

    [Fact]
    public async Task Failed_Flush_Requeues_Entries_Instead_Of_Losing_Them()
    {
        var (buffered, inner) = NewBufferedStore(batchSize: 10);
        inner.FailNextBatch = true;

        for (int i = 0; i < 5; i++)
        {
            await buffered.SaveAsync(NewEntry(), default);
        }

        var act = async () => await buffered.FlushAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
        inner.Saved.Should().BeEmpty();

        // The store recovered — a later flush persists everything.
        (await buffered.FlushAsync()).Should().Be(5);
        inner.Saved.Count.Should().Be(5);
    }

    [Fact]
    public async Task Background_Flusher_Drains_On_Interval_And_On_Shutdown()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CountingAuditStore>();
        services.AddMediantAuditBuffering<CountingAuditStore>(o =>
        {
            o.BatchSize = 50;
            o.FlushInterval = TimeSpan.FromMilliseconds(50);
        });
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IAuditStore>();
        store.Should().BeOfType<BufferedAuditStore<CountingAuditStore>>();

        var processor = provider.GetServices<IHostedService>().OfType<AuditFlushProcessor<CountingAuditStore>>().Single();
        await processor.StartAsync(default);

        var inner = provider.GetRequiredService<CountingAuditStore>();
        await store.SaveAsync(NewEntry(), default);

        // Interval flush picks the entry up without an explicit FlushAsync.
        await WaitUntilAsync(() => inner.Saved.Count == 1, TimeSpan.FromSeconds(5));

        // Shutdown flush persists what is still buffered.
        await store.SaveAsync(NewEntry(), default);
        await processor.StopAsync(default);
        inner.Saved.Count.Should().Be(2, "graceful shutdown must flush the remaining buffer");
    }

    [Fact]
    public async Task SaveBatch_Routes_Through_The_Buffer()
    {
        var (buffered, inner) = NewBufferedStore(batchSize: 2);

        await buffered.SaveBatchAsync([NewEntry(), NewEntry(), NewEntry()], default);
        inner.Saved.Should().BeEmpty();

        (await buffered.FlushAsync()).Should().Be(3);
        inner.BatchCalls.Should().Be(2, "3 entries at BatchSize=2 flush as 2 + 1");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(10);
        }
    }
}

/// <summary>Inner store double that counts round-trips and can fail on demand.</summary>
public sealed class CountingAuditStore : IAuditStore
{
    private readonly object _gate = new();
    private readonly List<AuditEntry> _saved = new();

    public bool FailNextBatch { get; set; }
    public int BatchCalls { get; private set; }

    public IReadOnlyList<AuditEntry> Saved
    {
        get { lock (_gate) { return _saved.ToList(); } }
    }

    public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken)
        => SaveBatchAsync([entry], cancellationToken);

    public ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            BatchCalls++;
            if (FailNextBatch)
            {
                FailNextBatch = false;
                throw new InvalidOperationException("audit store outage");
            }

            _saved.AddRange(entries);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AuditEntry> results = _saved
                .Where(e => query.RequestType is null || e.RequestType == query.RequestType)
                .ToList();
            return new(results);
        }
    }
}
