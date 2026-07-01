using Mediant.Abstractions;

namespace Mediant.Sample.ECommerce.DomainEvents;

public sealed record OrderCreatedEvent(Guid OrderId, string UserId, decimal TotalAmount) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

public sealed record OrderCancelledEvent(Guid OrderId, string UserId) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}

public sealed record PaymentProcessedEvent(Guid PaymentId, Guid OrderId, decimal Amount) : IDomainEvent
{
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
