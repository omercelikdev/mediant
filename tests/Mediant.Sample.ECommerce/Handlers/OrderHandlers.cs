using Mediant.Abstractions;
using Mediant.Results;
using Mediant.Sample.ECommerce.Commands;
using Mediant.Sample.ECommerce.Domain;
using Mediant.Sample.ECommerce.DomainEvents;
using Mediant.Sample.ECommerce.Infrastructure;
using Mediant.Sample.ECommerce.Queries;

namespace Mediant.Sample.ECommerce.Handlers;

public sealed class CreateOrderHandler : ICommandHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly InMemoryOrderRepository _repo;
    private readonly IPublisher _publisher;

    public CreateOrderHandler(InMemoryOrderRepository repo, IPublisher publisher)
    {
        _repo = repo;
        _publisher = publisher;
    }

    public async ValueTask<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var order = new Order
        {
            UserId = request.UserId,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice
            }).ToList(),
            Status = OrderStatus.Confirmed
        };

        await _repo.AddAsync(order, cancellationToken);
        await _publisher.Publish(
            new OrderCreatedEvent(order.Id, order.UserId, order.TotalAmount),
            cancellationToken);

        return order.Id;
    }
}

public sealed class CancelOrderHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly InMemoryOrderRepository _repo;
    private readonly IPublisher _publisher;

    public CancelOrderHandler(InMemoryOrderRepository repo, IPublisher publisher)
    {
        _repo = repo;
        _publisher = publisher;
    }

    public async ValueTask<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.Id, cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("Order.NotFound", $"Order {request.Id} not found"));
        }

        if (order.Status == OrderStatus.Paid)
        {
            return Result.Failure(Error.Conflict("Order.AlreadyPaid", "Cannot cancel a paid order"));
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(order, cancellationToken);
        await _publisher.Publish(new OrderCancelledEvent(order.Id, order.UserId), cancellationToken);

        return Result.Success();
    }
}

public sealed class ProcessPaymentHandler : ICommandHandler<ProcessPaymentCommand, Result<Guid>>
{
    private readonly InMemoryOrderRepository _repo;
    private readonly FakePaymentGateway _gateway;
    private readonly IPublisher _publisher;

    public ProcessPaymentHandler(InMemoryOrderRepository repo, FakePaymentGateway gateway, IPublisher publisher)
    {
        _repo = repo;
        _gateway = gateway;
        _publisher = publisher;
    }

    public async ValueTask<Result<Guid>> Handle(ProcessPaymentCommand request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            return Error.NotFound("Order.NotFound", $"Order {request.OrderId} not found");
        }

        var success = await _gateway.ProcessAsync(request.OrderId, request.Amount, request.CardNumber, cancellationToken);
        if (!success)
        {
            return Error.Failure("Payment.Failed", "Payment processing failed");
        }

        var payment = new Payment
        {
            OrderId = request.OrderId,
            Amount = request.Amount,
            Method = request.PaymentMethod,
            IsSuccessful = true
        };

        order.Status = OrderStatus.Paid;
        order.PaidAt = DateTimeOffset.UtcNow;
        await _repo.UpdateAsync(order, cancellationToken);
        await _publisher.Publish(
            new PaymentProcessedEvent(payment.Id, order.Id, payment.Amount),
            cancellationToken);

        return payment.Id;
    }
}

public sealed class GetOrderByIdHandler : IQueryHandler<GetOrderByIdQuery, Result<Order>>
{
    private readonly InMemoryOrderRepository _repo;

    public GetOrderByIdHandler(InMemoryOrderRepository repo) => _repo = repo;

    public async ValueTask<Result<Order>> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var order = await _repo.GetByIdAsync(request.Id, cancellationToken);
        return order is null
            ? Result<Order>.Failure(Error.NotFound("Order.NotFound", $"Order {request.Id} not found"))
            : order;
    }
}

public sealed class GetOrdersForUserHandler : IQueryHandler<GetOrdersForUserQuery, Result<List<Order>>>
{
    private readonly InMemoryOrderRepository _repo;

    public GetOrdersForUserHandler(InMemoryOrderRepository repo) => _repo = repo;

    public async ValueTask<Result<List<Order>>> Handle(GetOrdersForUserQuery request, CancellationToken cancellationToken)
    {
        var orders = await _repo.GetByUserIdAsync(request.UserId, cancellationToken);
        return orders;
    }
}

public sealed class ListOrdersHandler : IQueryHandler<ListOrdersQuery, Result<OrderPage>>
{
    public ValueTask<Result<OrderPage>> Handle(ListOrdersQuery request, CancellationToken cancellationToken)
        // Echo the bound values so an E2E test can assert defaults and query values flowed through.
        => new(new OrderPage(request.Cursor ?? "(none)", request.Size, Count: 0));
}

public sealed class SearchOrdersHandler : IStreamRequestHandler<SearchOrdersQuery, Order>
{
    private readonly InMemoryOrderRepository _repo;

    public SearchOrdersHandler(InMemoryOrderRepository repo) => _repo = repo;

    public async IAsyncEnumerable<Order> Handle(SearchOrdersQuery request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var orders = await _repo.SearchAsync(request.Status, request.UserId, cancellationToken);
        foreach (var order in orders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return order;
        }
    }
}
