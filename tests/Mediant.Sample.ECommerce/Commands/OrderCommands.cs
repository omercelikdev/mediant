using Mediant.Abstractions;
using Mediant.Attributes;
using Mediant.Behaviors.Attributes;
using Mediant.AspNetCore.Attributes;
using Mediant.Results;
using Mediant.Sample.ECommerce.Domain;

namespace Mediant.Sample.ECommerce.Commands;

[HttpEndpoint("POST", "/api/orders", Summary = "Create a new order", Tags = new[] { "Orders" })]
[Transactional]
[Auditable(IncludeRequestBody = true)]
public sealed record CreateOrderCommand : ICommand<Result<Guid>>
{
    public string UserId { get; init; } = string.Empty;
    public List<CreateOrderItemDto> Items { get; init; } = new();
}

public sealed record CreateOrderItemDto
{
    public string ProductId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
}

[HttpEndpoint("PUT", "/api/orders/{Id}/cancel", Summary = "Cancel an order", Tags = new[] { "Orders" })]
[Transactional]
[Auditable]
[Authorize(Roles = "Admin,Owner")]
public sealed record CancelOrderCommand : ICommand<Result>
{
    public Guid Id { get; init; }
}

[HttpEndpoint("POST", "/api/orders/{OrderId}/pay", Summary = "Process payment for an order", Tags = new[] { "Payments" })]
[Transactional]
[Idempotent(60)]
[Retryable(3)]
[Auditable(IncludeRequestBody = true)]
public sealed record ProcessPaymentCommand : ICommand<Result<Guid>>
{
    public Guid OrderId { get; init; }

    [SensitiveData]
    public string CardNumber { get; init; } = string.Empty;

    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = "CreditCard";
}
