using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.EntityFrameworkCore;

namespace Mediant.EntityFrameworkCore.Tests;

/// <summary>
/// End-to-end nested savepoint semantics (#141) against real SQLite savepoints via
/// <see cref="EfCoreUnitOfWork{TContext}"/>: with
/// <c>TransactionBehaviorOptions.NestedSavepoints</c> enabled, a failed nested command's writes
/// are unwound while the outer handler catches the failure and commits its own work.
/// </summary>
public sealed class EfNestedSavepointTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public EfNestedSavepointTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var context = new UowDbContext(
            new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ServiceProvider BuildProvider(bool nestedSavepoints)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<UowDbContext>(o => o.UseSqlite(_connection));
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(EfNestedSavepointTests).Assembly));
        services.AddMediantTransactions(o => o.NestedSavepoints = nestedSavepoints);
        services.AddMediantEfCoreUnitOfWork<UowDbContext>();
        return services.BuildServiceProvider();
    }

    private async Task<List<string>> PersistedOrderNamesAsync()
    {
        using var verify = new UowDbContext(
            new DbContextOptionsBuilder<UowDbContext>().UseSqlite(_connection).Options);
        return await verify.Orders.Select(o => o.Name).OrderBy(n => n).ToListAsync();
    }

    [Fact]
    public async Task Enabled_Failed_Inner_Writes_Are_Unwound_While_Outer_Work_Commits()
    {
        using var provider = BuildProvider(nestedSavepoints: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new NestedOuterCommand(SwallowInnerFailure: true));

        // Outer row committed; the failed inner command's row — written to the DB before the
        // failure — was rolled back to the savepoint.
        (await PersistedOrderNamesAsync()).Should().Equal("outer");
    }

    [Fact]
    public async Task Enabled_Successful_Inner_And_Outer_Writes_Both_Commit()
    {
        using var provider = BuildProvider(nestedSavepoints: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new NestedOuterCommand(SwallowInnerFailure: false, InnerShouldFail: false));

        (await PersistedOrderNamesAsync()).Should().Equal("inner", "outer");
    }

    [Fact]
    public async Task Disabled_Default_Keeps_Join_Semantics_Inner_Writes_Commit_With_The_Outer()
    {
        // Documents the as-is default: when the outer handler swallows the inner failure and
        // commits, the inner command's flushed writes are committed too (join semantics).
        using var provider = BuildProvider(nestedSavepoints: false);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new NestedOuterCommand(SwallowInnerFailure: true));

        (await PersistedOrderNamesAsync()).Should().Equal("inner", "outer");
    }

    [Fact]
    public async Task Enabled_Uncaught_Inner_Failure_Still_Rolls_Back_Everything()
    {
        using var provider = BuildProvider(nestedSavepoints: true);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Invoking(m => m.Send(new NestedOuterCommand(SwallowInnerFailure: false)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        (await PersistedOrderNamesAsync()).Should().BeEmpty();
    }
}

[Transactional]
public sealed record NestedOuterCommand(bool SwallowInnerFailure, bool InnerShouldFail = true) : ICommand<Guid>;

[Transactional]
public sealed record NestedInnerCommand(bool ShouldFail) : ICommand<Guid>;

internal sealed class NestedOuterCommandHandler : IRequestHandler<NestedOuterCommand, Guid>
{
    private readonly UowDbContext _db;
    private readonly IMediator _mediator;

    public NestedOuterCommandHandler(UowDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async ValueTask<Guid> Handle(NestedOuterCommand request, CancellationToken cancellationToken)
    {
        var order = new UowOrder { Id = Guid.NewGuid(), Name = "outer" };
        _db.Orders.Add(order);

        try
        {
            await _mediator.Send(new NestedInnerCommand(request.InnerShouldFail), cancellationToken);
        }
        catch (InvalidOperationException) when (request.SwallowInnerFailure)
        {
            // Business decision: the inner step is optional; keep the outer work.
        }

        return order.Id;
    }
}

internal sealed class NestedInnerCommandHandler : IRequestHandler<NestedInnerCommand, Guid>
{
    private readonly UowDbContext _db;

    public NestedInnerCommandHandler(UowDbContext db) => _db = db;

    public async ValueTask<Guid> Handle(NestedInnerCommand request, CancellationToken cancellationToken)
    {
        var order = new UowOrder { Id = Guid.NewGuid(), Name = "inner" };
        _db.Orders.Add(order);
        // Flush so the write is IN the database before the failure — proving the savepoint
        // rollback (not a mere tracker discard) is what unwinds it.
        await _db.SaveChangesAsync(cancellationToken);

        if (request.ShouldFail)
        {
            throw new InvalidOperationException("inner failed");
        }

        return order.Id;
    }
}
