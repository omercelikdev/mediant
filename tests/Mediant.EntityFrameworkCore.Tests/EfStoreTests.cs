using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.DependencyInjection;
using Mediant.Behaviors.Outbox;
using Mediant.DependencyInjection;
using Mediant.EntityFrameworkCore;

namespace Mediant.EntityFrameworkCore.Tests;

public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureMediantOutbox();
        modelBuilder.ConfigureMediantAudit();
    }
}

public class EfStoreTests
{
    private static TestDbContext NewContext(string dbName)
        => new(new DbContextOptionsBuilder<TestDbContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public async Task Outbox_Add_Survives_Save_And_Mark_Processed_Removes_From_Pending()
    {
        var db = NewContext(Guid.NewGuid().ToString());
        var store = new EfOutboxStore<TestDbContext>(db);
        var message = new OutboxMessage { Id = Guid.NewGuid(), NotificationType = "T", Payload = "{}", OccurredOn = DateTimeOffset.UtcNow };

        await store.AddAsync(message, default);
        await db.SaveChangesAsync();   // business commit persists the message atomically

        (await store.GetUnprocessedAsync(10, default)).Should().ContainSingle();

        await store.MarkProcessedAsync(message.Id, DateTimeOffset.UtcNow, default);
        (await store.GetUnprocessedAsync(10, default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Audit_Save_And_Query_RoundTrips_Including_Metadata()
    {
        var db = NewContext(Guid.NewGuid().ToString());
        var store = new EfAuditStore<TestDbContext>(db);
        var entry = new AuditEntry
        {
            CorrelationId = "corr-1",
            RequestType = "CreateOrder",
            UserId = "u1",
            IsSuccess = true,
            Metadata = { ["k1"] = "v1", ["k2"] = "v2" },
        };

        await store.SaveAsync(entry, default);

        var results = await store.QueryAsync(new AuditQuery { RequestType = "CreateOrder" }, default);
        var fetched = results.Should().ContainSingle().Subject;
        fetched.CorrelationId.Should().Be("corr-1");
        fetched.Metadata.Should().ContainKey("k1").WhoseValue.Should().Be("v1");
        fetched.Metadata.Should().ContainKey("k2");
    }

    [Fact]
    public async Task Outbox_EndToEnd_Through_Processor_With_Ef_Store()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<EfOutboxSink>();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(dbName), ServiceLifetime.Scoped);
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(EfStoreTests).Assembly));
        // Register the EF store BEFORE AddMediantOutbox so it wins over the in-memory default.
        services.AddMediantEfCoreOutboxStore<TestDbContext>();
        services.AddMediantOutbox();

        var provider = services.BuildServiceProvider();

        // Enqueue within a scope and commit (the business transaction).
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new EfOutboxEvent("durable"));
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync();
        }

        // Run the real processor — it opens its own scope/context and dispatches.
        var processor = new OutboxProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<OutboxProcessorOptions>>(),
            provider.GetRequiredService<ILogger<OutboxProcessor>>());
        await processor.ProcessPendingAsync(default);

        provider.GetRequiredService<EfOutboxSink>().Received.Should().ContainSingle().Which.Should().Be("durable");

        // The message is marked processed in the database.
        using var verifyScope = provider.CreateScope();
        var store = verifyScope.ServiceProvider.GetRequiredService<IOutboxStore>();
        (await store.GetUnprocessedAsync(10, default)).Should().BeEmpty();
    }
}

public sealed class EfOutboxSink
{
    private readonly object _gate = new();
    private readonly List<string> _received = new();
    public void Add(string s) { lock (_gate) { _received.Add(s); } }
    public IReadOnlyList<string> Received { get { lock (_gate) { return _received.ToList(); } } }
}

public sealed record EfOutboxEvent(string Message) : INotification;

internal sealed class EfOutboxEventHandler : INotificationHandler<EfOutboxEvent>
{
    private readonly EfOutboxSink _sink;
    public EfOutboxEventHandler(EfOutboxSink sink) => _sink = sink;
    public ValueTask Handle(EfOutboxEvent notification, CancellationToken cancellationToken)
    {
        _sink.Add(notification.Message);
        return ValueTask.CompletedTask;
    }
}
