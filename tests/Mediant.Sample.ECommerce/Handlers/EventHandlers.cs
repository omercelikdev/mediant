using Mediant.Abstractions;
using Mediant.Sample.ECommerce.DomainEvents;

namespace Mediant.Sample.ECommerce.Handlers;

public sealed class SendConfirmationEmailHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly ILogger<SendConfirmationEmailHandler> _logger;
    public SendConfirmationEmailHandler(ILogger<SendConfirmationEmailHandler> logger) => _logger = logger;

    public ValueTask Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Email] Confirmation sent for order {OrderId} to user {UserId}",
            notification.OrderId, notification.UserId);
        return ValueTask.CompletedTask;
    }
}

public sealed class UpdateInventoryHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly ILogger<UpdateInventoryHandler> _logger;
    public UpdateInventoryHandler(ILogger<UpdateInventoryHandler> logger) => _logger = logger;

    public ValueTask Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Inventory] Reserved stock for order {OrderId}", notification.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class IndexInSearchHandler : INotificationHandler<OrderCreatedEvent>
{
    private readonly ILogger<IndexInSearchHandler> _logger;
    public IndexInSearchHandler(ILogger<IndexInSearchHandler> logger) => _logger = logger;

    public ValueTask Handle(OrderCreatedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Search] Indexed order {OrderId}", notification.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class RestoreInventoryHandler : INotificationHandler<OrderCancelledEvent>
{
    private readonly ILogger<RestoreInventoryHandler> _logger;
    public RestoreInventoryHandler(ILogger<RestoreInventoryHandler> logger) => _logger = logger;

    public ValueTask Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Inventory] Restored stock for cancelled order {OrderId}", notification.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class NotifyUserCancellationHandler : INotificationHandler<OrderCancelledEvent>
{
    private readonly ILogger<NotifyUserCancellationHandler> _logger;
    public NotifyUserCancellationHandler(ILogger<NotifyUserCancellationHandler> logger) => _logger = logger;

    public ValueTask Handle(OrderCancelledEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Email] Cancellation notification sent for order {OrderId}", notification.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class UpdateOrderStatusHandler : INotificationHandler<PaymentProcessedEvent>
{
    private readonly ILogger<UpdateOrderStatusHandler> _logger;
    public UpdateOrderStatusHandler(ILogger<UpdateOrderStatusHandler> logger) => _logger = logger;

    public ValueTask Handle(PaymentProcessedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Status] Order {OrderId} marked as paid", notification.OrderId);
        return ValueTask.CompletedTask;
    }
}

public sealed class SendReceiptHandler : INotificationHandler<PaymentProcessedEvent>
{
    private readonly ILogger<SendReceiptHandler> _logger;
    public SendReceiptHandler(ILogger<SendReceiptHandler> logger) => _logger = logger;

    public ValueTask Handle(PaymentProcessedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Email] Receipt sent for payment {PaymentId} on order {OrderId}",
            notification.PaymentId, notification.OrderId);
        return ValueTask.CompletedTask;
    }
}
