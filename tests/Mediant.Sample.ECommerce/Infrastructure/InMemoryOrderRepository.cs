using System.Collections.Concurrent;
using Mediant.Sample.ECommerce.Domain;

namespace Mediant.Sample.ECommerce.Infrastructure;

public sealed class InMemoryOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult(order);
    }

    public Task<List<Order>> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        var orders = _orders.Values
            .Where(o => string.Equals(o.UserId, userId, StringComparison.Ordinal))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();
        return Task.FromResult(orders);
    }

    public Task<List<Order>> SearchAsync(string? status, string? userId, CancellationToken ct = default)
    {
        var query = _orders.Values.AsEnumerable();
        if (status is not null && Enum.TryParse<OrderStatus>(status, true, out var s))
        {
            query = query.Where(o => o.Status == s);
        }
        if (userId is not null)
        {
            query = query.Where(o => string.Equals(o.UserId, userId, StringComparison.Ordinal));
        }
        return Task.FromResult(query.ToList());
    }

    public Task AddAsync(Order order, CancellationToken ct = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryUnitOfWork : Mediant.Abstractions.IUnitOfWork
{
    private int _beginCount;
    private int _commitCount;

    public int BeginCount => _beginCount;
    public int CommitCount => _commitCount;

    public ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _beginCount);
        Console.WriteLine($"[UoW] BeginTransaction (total: {_beginCount})");
        return ValueTask.CompletedTask;
    }

    public ValueTask SaveChangesAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[UoW] SaveChanges");
        return ValueTask.CompletedTask;
    }

    public ValueTask CommitAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _commitCount);
        Console.WriteLine($"[UoW] Commit (total: {_commitCount})");
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("[UoW] Rollback");
        return ValueTask.CompletedTask;
    }

    public ValueTask CreateSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public ValueTask RollbackToSavepointAsync(string name, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public void Reset()
    {
        Interlocked.Exchange(ref _beginCount, 0);
        Interlocked.Exchange(ref _commitCount, 0);
    }
}

public sealed class FakePaymentGateway
{
    private int _callCount;

    public Task<bool> ProcessAsync(Guid orderId, decimal amount, string cardNumber, CancellationToken ct = default)
    {
        var count = Interlocked.Increment(ref _callCount);
        // Simulate transient failures for first 2 calls to test retry behavior
        if (count <= 2)
        {
            throw new TimeoutException("Payment gateway temporarily unavailable");
        }
        return Task.FromResult(true);
    }

    public void Reset() => Interlocked.Exchange(ref _callCount, 0);
}
