namespace Mediant.Sample.ECommerce.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Paid,
    Cancelled
}

public sealed class OrderItem
{
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal TotalPrice => Quantity * UnitPrice;
}

public sealed class Order
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = string.Empty;
    public List<OrderItem> Items { get; init; } = new();
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount => Items.Sum(x => x.TotalPrice);
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
}

public sealed class Payment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; init; }
    public decimal Amount { get; init; }
    public string Method { get; init; } = "CreditCard";
    public bool IsSuccessful { get; set; }
    public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
}
