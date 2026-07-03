using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mediant.Abstractions;

namespace Mediant.EntityFrameworkCore.Tests;

/// <summary>
/// SQLite-backed context for the claim tests. SQLite cannot compare DateTimeOffset columns, so
/// they are mapped to UTC ticks — this only affects the test database, not the library mapping.
/// </summary>
public sealed class SqliteTestDbContext : DbContext
{
    public SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureMediantOutbox();
        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.Property(e => e.OccurredOn).HasConversion(
                v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            entity.Property(e => e.ProcessedOn).HasConversion(
                v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
            entity.Property(e => e.ClaimedUntil).HasConversion(
                v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : (DateTimeOffset?)null);
        });
    }
}

/// <summary>
/// Claim/lease coordination tests for <see cref="EfOutboxStore{TContext}"/>. These run on SQLite
/// (shared in-memory connection) because the claim uses a set-based <c>ExecuteUpdate</c> that the
/// EF InMemory provider cannot translate.
/// </summary>
public sealed class EfOutboxClaimTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SqliteTestDbContext> _contextOptions;

    public EfOutboxClaimTests()
    {
        // One shared in-memory database for all contexts in the test.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _contextOptions = new DbContextOptionsBuilder<SqliteTestDbContext>().UseSqlite(_connection).Options;
        using var db = new SqliteTestDbContext(_contextOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private SqliteTestDbContext NewContext() => new(_contextOptions);

    private async Task SeedAsync(params OutboxMessage[] messages)
    {
        using var db = NewContext();
        db.AddRange(messages.AsEnumerable());
        await db.SaveChangesAsync();
    }

    private static OutboxMessage NewMessage(int ageSeconds = 60, int attempts = 0) => new()
    {
        Id = Guid.NewGuid(),
        NotificationType = "T",
        Payload = "{}",
        OccurredOn = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds),
        Attempts = attempts,
    };

    [Fact]
    public async Task Two_Owners_Never_Claim_The_Same_Message()
    {
        var messages = Enumerable.Range(0, 20).Select(i => NewMessage(ageSeconds: 100 - i)).ToArray();
        await SeedAsync(messages);

        using var dbA = NewContext();
        using var dbB = NewContext();
        var storeA = new EfOutboxStore<SqliteTestDbContext>(dbA);
        var storeB = new EfOutboxStore<SqliteTestDbContext>(dbB);

        var claimedA = await storeA.ClaimPendingAsync("owner-A", batchSize: 20, maxAttempts: 5, TimeSpan.FromMinutes(1), default);
        var claimedB = await storeB.ClaimPendingAsync("owner-B", batchSize: 20, maxAttempts: 5, TimeSpan.FromMinutes(1), default);

        claimedA.Should().NotBeEmpty();
        claimedA.Select(m => m.Id).Intersect(claimedB.Select(m => m.Id)).Should().BeEmpty(
            "a message claimed under a live lease must not be handed to another owner");
        (claimedA.Count + claimedB.Count).Should().BeLessThanOrEqualTo(20);
    }

    [Fact]
    public async Task Expired_Lease_Is_Reclaimable_By_Another_Owner()
    {
        var message = NewMessage();
        await SeedAsync(message);

        using (var db = NewContext())
        {
            var store = new EfOutboxStore<SqliteTestDbContext>(db);
            // Zero-length lease: expires immediately (crashed-owner simulation).
            var claimed = await store.ClaimPendingAsync("owner-dead", 10, 5, TimeSpan.Zero, default);
            claimed.Should().ContainSingle();
        }

        using (var db = NewContext())
        {
            var store = new EfOutboxStore<SqliteTestDbContext>(db);
            var reclaimed = await store.ClaimPendingAsync("owner-live", 10, 5, TimeSpan.FromMinutes(1), default);
            reclaimed.Should().ContainSingle("an expired lease means the previous owner crashed and the message must be reclaimable");
            reclaimed[0].Id.Should().Be(message.Id);
        }
    }

    [Fact]
    public async Task Live_Lease_Blocks_Other_Owners_Until_Released()
    {
        var message = NewMessage();
        await SeedAsync(message);

        using var dbA = NewContext();
        var storeA = new EfOutboxStore<SqliteTestDbContext>(dbA);
        (await storeA.ClaimPendingAsync("owner-A", 10, 5, TimeSpan.FromMinutes(5), default)).Should().ContainSingle();

        using (var dbB = NewContext())
        {
            var storeB = new EfOutboxStore<SqliteTestDbContext>(dbB);
            (await storeB.ClaimPendingAsync("owner-B", 10, 5, TimeSpan.FromMinutes(5), default)).Should().BeEmpty();
        }

        // A failed dispatch releases the claim so any instance may retry immediately.
        await storeA.MarkFailedAsync(message.Id, attempts: 1, error: "boom", default);

        using (var dbB = NewContext())
        {
            var storeB = new EfOutboxStore<SqliteTestDbContext>(dbB);
            (await storeB.ClaimPendingAsync("owner-B", 10, 5, TimeSpan.FromMinutes(5), default)).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Claim_Excludes_Processed_And_MaxAttempts_Messages()
    {
        var healthy = NewMessage(ageSeconds: 10);
        var poisoned = NewMessage(ageSeconds: 100, attempts: 5);
        var processed = NewMessage(ageSeconds: 50);
        processed.ProcessedOn = DateTimeOffset.UtcNow;
        await SeedAsync(healthy, poisoned, processed);

        using var db = NewContext();
        var store = new EfOutboxStore<SqliteTestDbContext>(db);
        var claimed = await store.ClaimPendingAsync("owner", 10, maxAttempts: 5, TimeSpan.FromMinutes(1), default);

        claimed.Should().ContainSingle("poisoned (attempts >= max) and processed messages must not be claimed")
            .Which.Id.Should().Be(healthy.Id);
    }

    [Fact]
    public async Task MarkProcessed_Clears_The_Claim()
    {
        var message = NewMessage();
        await SeedAsync(message);

        using var db = NewContext();
        var store = new EfOutboxStore<SqliteTestDbContext>(db);
        (await store.ClaimPendingAsync("owner", 10, 5, TimeSpan.FromMinutes(5), default)).Should().ContainSingle();

        await store.MarkProcessedAsync(message.Id, DateTimeOffset.UtcNow, default);

        using var verify = NewContext();
        var row = await verify.Set<OutboxMessage>().SingleAsync(m => m.Id == message.Id);
        row.ProcessedOn.Should().NotBeNull();
        row.ClaimedBy.Should().BeNull();
        row.ClaimedUntil.Should().BeNull();
    }
}
