using Mediant.Abstractions;
using Mediant.Results;
using Mediant.Sample.ECommerce.Commands;
using Mediant.Sample.ECommerce.Domain;
using Mediant.Sample.ECommerce.DomainEvents;
using Mediant.Sample.ECommerce.Infrastructure;

namespace Mediant.Sample.ECommerce.Handlers;

/// <summary>
/// Creates order then dispatches nested ConfirmOrderCommand through mediator.
/// Demonstrates: nested transaction, post-commit queue, IAuditableRequest.
/// </summary>
public sealed class CreateAndConfirmOrderHandler : ICommandHandler<CreateAndConfirmOrderCommand, Result<Guid>>
{
    private readonly InMemoryOrderRepository _repo;
    private readonly IMediator _mediator;
    private readonly IPostCommitTaskQueue _postCommitQueue;

    public CreateAndConfirmOrderHandler(
        InMemoryOrderRepository repo,
        IMediator mediator,
        IPostCommitTaskQueue postCommitQueue)
    {
        _repo = repo;
        _mediator = mediator;
        _postCommitQueue = postCommitQueue;
    }

    public async ValueTask<Result<Guid>> Handle(CreateAndConfirmOrderCommand request, CancellationToken cancellationToken)
    {
        // 1) Create the order
        var order = new Order
        {
            UserId = request.UserId,
            Items = new List<OrderItem>
            {
                new()
                {
                    ProductId = "PROD-001",
                    ProductName = request.ProductName,
                    Quantity = request.Quantity,
                    UnitPrice = request.UnitPrice
                }
            },
            Status = OrderStatus.Pending
        };

        await _repo.AddAsync(order, cancellationToken);

        // 2) Dispatch nested command — should join outer transaction, NOT create new one
        var confirmResult = await _mediator.Send(new ConfirmOrderCommand(order.Id), cancellationToken);
        if (!confirmResult.IsSuccess)
        {
            return Result<Guid>.Failure(confirmResult.Error);
        }

        // 3) Enqueue post-commit task — should execute only after transaction commits
        _postCommitQueue.Enqueue(async ct =>
        {
            // In real app: send confirmation email, publish to event bus, etc.
            await Task.CompletedTask;
        });

        return order.Id;
    }
}

/// <summary>
/// Confirms an order. Called as nested command — participates in outer transaction.
/// </summary>
public sealed class ConfirmOrderHandler : ICommandHandler<ConfirmOrderCommand>
{
    private readonly InMemoryOrderRepository _repo;
    private readonly IPublisher _publisher;

    public ConfirmOrderHandler(InMemoryOrderRepository repo, IPublisher publisher)
    {
        _repo = repo;
        _publisher = publisher;
    }

    public async ValueTask<Result> Handle(ConfirmOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"Order {request.OrderId} not found"));
        }

        order.Status = OrderStatus.Confirmed;
        await _repo.UpdateAsync(order, cancellationToken);

        await _publisher.Publish(
            new OrderCreatedEvent(order.Id, order.UserId, order.TotalAmount),
            cancellationToken);

        return Result.Success();
    }
}
