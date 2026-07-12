using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.EntityFrameworkCore;

namespace Mediant.EntityFrameworkCore.Tests;

/// <summary>
/// Regression for the shared-context rollback + audit hazard (#138): a failed
/// <c>[Transactional]</c> + audited command must leave NO business rows behind while still
/// persisting the failure audit entry. Before the change-tracker-clearing rollback in
/// <see cref="EfCoreUnitOfWork{TContext}"/>, the unbuffered <see cref="EfAuditStore{TContext}"/>'s
/// <c>SaveChangesAsync</c> on the same scoped context re-flushed the rolled-back handler
/// entities outside any transaction.
/// </summary>
public sealed class EfAuditRollbackRegressionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    public EfAuditRollbackRegressionTests()
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
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(EfAuditRollbackRegressionTests).Assembly));
        // The exact setup from the issue: business data and the UNBUFFERED audit store share one
        // scoped context. EF stores registered before the behaviors so they win the TryAdd.
        services.AddMediantEfCoreAuditStore<UowDbContext>();
        services.AddMediantEfCoreUnitOfWork<UowDbContext>();
        services.AddMediantAuditing();
        services.AddMediantTransactions();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Failed_Transactional_Audited_Command_Persists_The_Failure_Audit_But_No_Business_Rows()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Invoking(m => m.Send(new AuditedCreateOrderCommand("doomed", Fail: true)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("audited handler failed");

        using var verifyScope = _provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<UowDbContext>();

        // The core of #138: the failure-audit SaveChanges on the shared context must NOT
        // resurrect the rolled-back handler entities.
        (await db.Orders.CountAsync()).Should().Be(0);

        var audit = await db.Set<AuditEntry>().SingleAsync();
        audit.IsSuccess.Should().BeFalse();
        audit.ErrorMessage.Should().Be("audited handler failed");
        audit.RequestType.Should().Contain(nameof(AuditedCreateOrderCommand));
    }

    [Fact]
    public async Task Successful_Transactional_Audited_Command_Persists_Business_Row_And_Success_Audit()
    {
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Send(new AuditedCreateOrderCommand("ok", Fail: false));

        using var verifyScope = _provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<UowDbContext>();
        (await db.Orders.CountAsync()).Should().Be(1);

        var audit = await db.Set<AuditEntry>().SingleAsync();
        audit.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_Command_Then_Second_Command_In_The_Same_Scope_Stays_Uncontaminated()
    {
        // A rolled-back command must not leak tracked entities into a later command that
        // commits on the same scoped context (same DI scope = same DbContext instance).
        using var scope = _provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.Invoking(m => m.Send(new AuditedCreateOrderCommand("doomed", Fail: true)).AsTask())
            .Should().ThrowAsync<InvalidOperationException>();

        await mediator.Send(new AuditedCreateOrderCommand("survivor", Fail: false));

        using var verifyScope = _provider.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<UowDbContext>();
        var names = await db.Orders.Select(o => o.Name).ToListAsync();
        names.Should().ContainSingle().Which.Should().Be("survivor");
    }
}

[Transactional]
[Auditable]
public sealed record AuditedCreateOrderCommand(string Name, bool Fail) : ICommand<Guid>;

internal sealed class AuditedCreateOrderHandler : IRequestHandler<AuditedCreateOrderCommand, Guid>
{
    private readonly UowDbContext _db;

    public AuditedCreateOrderHandler(UowDbContext db) => _db = db;

    public ValueTask<Guid> Handle(AuditedCreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new UowOrder { Id = Guid.NewGuid(), Name = request.Name };
        _db.Orders.Add(order);

        if (request.Fail)
        {
            throw new InvalidOperationException("audited handler failed");
        }

        return ValueTask.FromResult(order.Id);
    }
}
