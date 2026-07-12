using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.EntityFrameworkCore;

namespace Mediant.EntityFrameworkCore.Tests;

public sealed class UowDbContext : DbContext
{
    public UowDbContext(DbContextOptions<UowDbContext> options) : base(options) { }

    public DbSet<UowOrder> Orders => Set<UowOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UowOrder>().HasKey(o => o.Id);
        modelBuilder.ConfigureMediantOutbox();
        modelBuilder.ConfigureMediantAudit();
    }
}

public sealed class UowOrder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class EfCoreUnitOfWorkTests : IDisposable
{
    // A single open in-memory SQLite connection keeps the database alive and gives every
    // context real relational transactions.
    private readonly SqliteConnection _connection;

    public EfCoreUnitOfWorkTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private UowDbContext NewContext()
        => new(new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task Commit_Persists_Writes_Made_Inside_The_Transaction()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.BeginTransactionAsync(default);
        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "committed" });
        await uow.SaveChangesAsync(default);
        await uow.CommitAsync(default);

        using var verify = NewContext();
        (await verify.Orders.CountAsync()).Should().Be(1);
        context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task Rollback_Discards_Writes_And_Clears_The_ChangeTracker()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.BeginTransactionAsync(default);
        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "doomed" });
        await uow.SaveChangesAsync(default);
        await uow.RollbackAsync(default);

        // Nothing hit the database…
        using var verify = NewContext();
        (await verify.Orders.CountAsync()).Should().Be(0);

        // …and the tracker is empty, so a later SaveChanges (e.g. a failure-audit write on the
        // same context) can never re-flush the rolled-back entities.
        context.ChangeTracker.Entries().Should().BeEmpty();
        await context.SaveChangesAsync();
        (await verify.Orders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rollback_Clears_Tracked_Entities_Even_When_SaveChanges_Was_Never_Called()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.BeginTransactionAsync(default);
        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "never-saved" });
        await uow.RollbackAsync(default);

        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task BeginTransaction_Is_A_NoOp_When_A_Transaction_Is_Already_Open()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.BeginTransactionAsync(default);
        var first = context.Database.CurrentTransaction;

        // Second Begin (execution-strategy retry / re-entry) must not throw or replace it.
        await uow.BeginTransactionAsync(default);
        context.Database.CurrentTransaction.Should().BeSameAs(first);

        await uow.RollbackAsync(default);
    }

    [Fact]
    public async Task Commit_And_Rollback_Without_A_Transaction_Are_NoOps()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.Invoking(u => u.CommitAsync(default).AsTask()).Should().NotThrowAsync();
        await uow.Invoking(u => u.RollbackAsync(default).AsTask()).Should().NotThrowAsync();
        await uow.Invoking(u => u.CreateSavepointAsync("sp", default).AsTask()).Should().NotThrowAsync();
        await uow.Invoking(u => u.RollbackToSavepointAsync("sp", default).AsTask()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task RollbackToSavepoint_Unwinds_Only_The_Work_After_The_Savepoint()
    {
        using var context = NewContext();
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        await uow.BeginTransactionAsync(default);

        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "outer" });
        await uow.SaveChangesAsync(default);

        await uow.CreateSavepointAsync("inner", default);
        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "inner" });
        await uow.SaveChangesAsync(default);

        await uow.RollbackToSavepointAsync("inner", default);
        await uow.CommitAsync(default);

        using var verify = NewContext();
        var names = await verify.Orders.Select(o => o.Name).ToListAsync();
        names.Should().ContainSingle().Which.Should().Be("outer");
    }

    [Fact]
    public async Task NonRelational_Provider_Skips_Transactions_But_Still_Flushes()
    {
        using var context = new UowDbContext(new DbContextOptionsBuilder<UowDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var uow = new EfCoreUnitOfWork<UowDbContext>(context);

        // InMemory throws on BeginTransaction; the UoW must degrade to no-op transactions so
        // [Transactional] handlers stay testable.
        await uow.BeginTransactionAsync(default);
        context.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "in-memory" });
        await uow.SaveChangesAsync(default);
        await uow.CommitAsync(default);

        (await context.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void Registration_Is_Scoped_And_Respects_An_Existing_IUnitOfWork()
    {
        var services = new ServiceCollection();
        services.AddDbContext<UowDbContext>(o => o.UseSqlite(_connection));
        services.AddMediantEfCoreUnitOfWork<UowDbContext>();

        var descriptor = services.Single(d => d.ServiceType == typeof(IUnitOfWork));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be<EfCoreUnitOfWork<UowDbContext>>();

        // TryAdd semantics: a custom IUnitOfWork registered first wins.
        var custom = new ServiceCollection();
        custom.AddScoped<IUnitOfWork, FakeUnitOfWork>();
        custom.AddMediantEfCoreUnitOfWork<UowDbContext>();
        custom.Single(d => d.ServiceType == typeof(IUnitOfWork))
            .ImplementationType.Should().Be<FakeUnitOfWork>();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public ValueTask BeginTransactionAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

/// <summary>
/// End-to-end: the real mediator pipeline with <c>AddMediantTransactions()</c> +
/// <c>AddMediantEfCoreUnitOfWork</c> against SQLite — the direct-DbContext (no repository)
/// clean-architecture setup the package is meant to serve out of the box.
/// </summary>
public sealed class EfCoreUnitOfWorkPipelineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EfCoreUnitOfWorkPipelineTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (var context = new UowDbContext(
            new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options))
        {
            context.Database.EnsureCreated();
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<UowDbContext>(o => o.UseSqlite(_connection));
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(EfCoreUnitOfWorkPipelineTests).Assembly));
        services.AddMediantTransactions();
        services.AddMediantEfCoreUnitOfWork<UowDbContext>();
        // EF outbox store BEFORE AddMediantOutbox so the durable store wins the TryAdd.
        services.AddMediantEfCoreOutboxStore<UowDbContext>();
        services.AddMediantOutbox();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Transactional_Command_Commits_Business_Write_And_Outbox_Message_Atomically()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // The handler writes through the context and enqueues an outbox message but never calls
        // SaveChanges — TransactionBehavior's flush + commit must persist both.
        await mediator.Send(new CreateUowOrderCommand("atomic", Fail: false));

        using var verifyScope = _provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<UowDbContext>();
        (await db.Orders.CountAsync()).Should().Be(1);
        (await db.Set<OutboxMessage>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Failing_Transactional_Command_Persists_Nothing_And_Leaves_A_Clean_Tracker()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Invoking(m => m.Send(new CreateUowOrderCommand("doomed", Fail: true)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");

        using (var verifyScope = _provider.CreateScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<UowDbContext>();
            (await db.Orders.CountAsync()).Should().Be(0);
            (await db.Set<OutboxMessage>().CountAsync()).Should().Be(0);
        }

        // The scope's context must not be left holding the rolled-back entities: a later
        // SaveChanges on the same scoped context (audit, another command) must not resurrect them.
        var scopedDb = scope.ServiceProvider.GetRequiredService<UowDbContext>();
        scopedDb.ChangeTracker.Entries().Should().BeEmpty();
        await scopedDb.SaveChangesAsync();

        using var finalScope = _provider.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<UowDbContext>();
        (await finalDb.Orders.CountAsync()).Should().Be(0);
    }
}

[Transactional]
public sealed record CreateUowOrderCommand(string Name, bool Fail) : ICommand<Guid>;

internal sealed class CreateUowOrderHandler : IRequestHandler<CreateUowOrderCommand, Guid>
{
    private readonly UowDbContext _db;
    private readonly IOutbox _outbox;

    public CreateUowOrderHandler(UowDbContext db, IOutbox outbox)
    {
        _db = db;
        _outbox = outbox;
    }

    public async ValueTask<Guid> Handle(CreateUowOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new UowOrder { Id = Guid.NewGuid(), Name = request.Name };
        _db.Orders.Add(order);
        await _outbox.EnqueueAsync(new UowOrderCreated(order.Id), cancellationToken);

        if (request.Fail)
        {
            throw new InvalidOperationException("handler failed");
        }

        // Intentionally no SaveChanges — TransactionBehavior's safety net owns the flush.
        return order.Id;
    }
}

public sealed record UowOrderCreated(Guid OrderId) : INotification;

internal sealed class UowOrderCreatedHandler : INotificationHandler<UowOrderCreated>
{
    public ValueTask Handle(UowOrderCreated notification, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
