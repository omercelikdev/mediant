using Mediant.Abstractions;
using Mediant.AspNetCore.Attributes;
using Mediant.Behaviors.Attributes;
using Mediant.Results;

namespace Mediant.Sample.ECommerce.Commands;

/// <summary>
/// Creates an order AND dispatches a nested ConfirmOrderCommand through the mediator.
/// Tests: nested transaction (single BeginTransaction), post-commit task queue.
/// </summary>
[HttpEndpoint("POST", "/api/orders/create-and-confirm", Summary = "Create and auto-confirm order (nested transaction test)", Tags = new[] { "Advanced" })]
[Transactional]
[Auditable(IncludeRequestBody = true)]
public sealed record CreateAndConfirmOrderCommand : ICommand<Result<Guid>>, IAuditableRequest
{
    public string UserId { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }

    // IAuditableRequest metadata
    public string ActionName => "Order.CreateAndConfirm";
    public string? EntityType => "Order";
    public string? EntityId => null;
    public object? AuditMetadata => new { ProductName, Quantity, UnitPrice };
}

/// <summary>
/// Confirms an order — called as nested command from CreateAndConfirmOrderCommand.
/// Both are [Transactional], but only the outer one should open a transaction.
/// </summary>
[Transactional]
public sealed record ConfirmOrderCommand(Guid OrderId) : ICommand<Result>;
