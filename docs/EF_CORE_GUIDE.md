# EF Core Integration Guide

How to implement `IUnitOfWork` with Entity Framework Core for proper transaction management.

## Recommended IUnitOfWork Implementation

```csharp
public sealed class EfCoreUnitOfWork(DbContext context) : IUnitOfWork
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
        if (context.Database.CurrentTransaction is null) return;
        await context.Database.CurrentTransaction.RollbackAsync(cancellationToken);
    }

    public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken)
        => new(context.Database.CurrentTransaction?.CreateSavepointAsync(name, cancellationToken) ?? Task.CompletedTask);

    public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken)
        => new(context.Database.CurrentTransaction?.RollbackToSavepointAsync(name, cancellationToken) ?? Task.CompletedTask);
}
```

## Key Design Decisions

### Nested Transactions

`TransactionBehavior` automatically detects nested transaction scopes using `AsyncLocal<bool>`. When Handler A dispatches a command to Handler B (both `[Transactional]`), only the outermost behavior calls `BeginTransaction/Commit`. The inner handler participates in the same transaction.

### Auto SaveChanges

`TransactionBehavior` calls `IUnitOfWork.SaveChangesAsync()` before `CommitAsync()`. This ensures EF Core's change tracker is flushed even if the handler forgets to call `SaveChangesAsync()`.

### Post-Commit Tasks

Use `IPostCommitTaskQueue` to enqueue fire-and-forget tasks (emails, events) that should only run after the transaction commits:

```csharp
public class CreateOrderHandler(
    AppDbContext db,
    IPostCommitTaskQueue postCommit) : ICommandHandler<CreateOrderCommand>
{
    public async ValueTask<Result> Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        db.Orders.Add(new Order { ... });
        // No need to call SaveChangesAsync — TransactionBehavior does it

        postCommit.Enqueue(ct => emailService.SendConfirmationAsync(cmd.Email, ct));

        return Result.Success();
    }
}
```

### Execution Strategy (Transient Retry)

For providers with retry policies (PostgreSQL/Npgsql, SQL Server), wrap transaction operations in the execution strategy. The recommended approach is to configure the strategy in `IUnitOfWork.BeginTransactionAsync`:

```csharp
public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
{
    if (context.Database.CurrentTransaction is not null) return;

    // The execution strategy handles transient retries
    var strategy = context.Database.CreateExecutionStrategy();
    await strategy.ExecuteAsync(async ct =>
    {
        await context.Database.BeginTransactionAsync(ct);
    }, cancellationToken);
}
```

> **Note:** When using execution strategy with explicit transactions, the entire operation may be retried. Ensure your handlers are idempotent or use the `[Idempotent]` attribute.

## DI Registration

```csharp
services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(connectionString));
services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
services.AddMediantTransactions(); // Registers TransactionBehavior + PostCommitTaskQueue
```
