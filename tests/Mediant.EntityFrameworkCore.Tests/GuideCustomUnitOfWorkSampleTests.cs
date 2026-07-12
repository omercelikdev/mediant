using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;

namespace Mediant.EntityFrameworkCore.Tests;

/// <summary>
/// Guards the custom unit-of-work template in <c>docs/EF_CORE_GUIDE.md</c> against drift (#140):
/// the class below mirrors the guide sample (concrete context injected — a <c>DbContext</c>-typed
/// constructor cannot resolve, since <c>AddDbContext&lt;T&gt;</c> registers only the concrete
/// type), and the tests prove the documented registration resolves and behaves.
/// </summary>
public sealed class GuideCustomUnitOfWorkSampleTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public GuideCustomUnitOfWorkSampleTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = new UowDbContext(
            new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    // The guide's "Custom IUnitOfWork Implementation" sample, with AppDbContext → UowDbContext.
    private sealed class AppUnitOfWork(UowDbContext context) : IUnitOfWork
    {
        public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
        {
            // Skip if already in a transaction (e.g., execution strategy retry)
            if (context.Database.CurrentTransaction is not null) return;

            await context.Database.BeginTransactionAsync(cancellationToken);
        }

        public async ValueTask SaveChangesAsync(CancellationToken cancellationToken)
        {
            // Flush change tracker to database before commit
            await context.SaveChangesAsync(cancellationToken);
        }

        public async ValueTask CommitAsync(CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is null) return;
            await context.Database.CurrentTransaction.CommitAsync(cancellationToken);
        }

        public async ValueTask RollbackAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (context.Database.CurrentTransaction is null) return;
                await context.Database.CurrentTransaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                // REQUIRED: rollback does not detach tracked entities. Without this, a later
                // SaveChanges on the same scoped context (e.g. EfAuditStore persisting the
                // failure audit entry) re-flushes the rolled-back entities OUTSIDE any
                // transaction — leaking half-done data from a failed command.
                context.ChangeTracker.Clear();
            }
        }

        public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken)
            => new(context.Database.CurrentTransaction?.CreateSavepointAsync(name, cancellationToken) ?? Task.CompletedTask);

        public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
            => new(context.Database.CurrentTransaction?.RollbackToSavepointAsync(name, cancellationToken) ?? Task.CompletedTask);
    }

    [Fact]
    public async Task The_Documented_Registration_Resolves_And_Commits()
    {
        // The guide's "DI Registration (custom implementation)" snippet.
        var services = new ServiceCollection();
        services.AddDbContext<UowDbContext>(opts => opts.UseSqlite(_connection));
        services.AddScoped<IUnitOfWork, AppUnitOfWork>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Resolving must not throw — this is exactly where the old DbContext-typed sample broke.
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var db = scope.ServiceProvider.GetRequiredService<UowDbContext>();
        await uow.BeginTransactionAsync(default);
        db.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "guide-sample" });
        await uow.SaveChangesAsync(default);
        await uow.CommitAsync(default);

        using var verify = new UowDbContext(
            new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options);
        (await verify.Orders.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task The_Sample_Rollback_Clears_The_Change_Tracker_As_The_Guide_Requires()
    {
        var services = new ServiceCollection();
        services.AddDbContext<UowDbContext>(opts => opts.UseSqlite(_connection));
        services.AddScoped<IUnitOfWork, AppUnitOfWork>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<UowDbContext>();

        await uow.BeginTransactionAsync(default);
        db.Orders.Add(new UowOrder { Id = Guid.NewGuid(), Name = "rolled-back" });
        await uow.RollbackAsync(default);

        db.ChangeTracker.Entries().Should().BeEmpty();
    }
}
