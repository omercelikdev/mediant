using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Notifications;

public class PolymorphicNotificationTests
{
    [Fact]
    public async Task Should_Invoke_Base_Handler_When_Polymorphic_Enabled()
    {
        var baseHandled = new List<string>();
        var derivedHandled = new List<string>();

        var services = new ServiceCollection();
        services.AddMediant(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PolymorphicNotificationTests).Assembly);
            cfg.EnablePolymorphicNotifications = true;
        });
        services.AddSingleton<INotificationHandler<BaseOrderEvent>>(
            new TrackingHandler<BaseOrderEvent>(baseHandled));
        services.AddSingleton<INotificationHandler<OrderCreatedEvent>>(
            new TrackingHandler<OrderCreatedEvent>(derivedHandled));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderCreatedEvent("order-1"));

        derivedHandled.Should().ContainSingle("derived handler should be called");
        baseHandled.Should().ContainSingle("base handler should also be called with polymorphic dispatch");
    }

    [Fact]
    public async Task Should_Not_Invoke_Base_Handler_When_Polymorphic_Disabled()
    {
        var baseHandled = new List<string>();
        var derivedHandled = new List<string>();

        var services = new ServiceCollection();
        services.AddMediant(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PolymorphicNotificationTests).Assembly);
            cfg.EnablePolymorphicNotifications = false; // default
        });
        services.AddSingleton<INotificationHandler<BaseOrderEvent>>(
            new TrackingHandler<BaseOrderEvent>(baseHandled));
        services.AddSingleton<INotificationHandler<OrderCreatedEvent>>(
            new TrackingHandler<OrderCreatedEvent>(derivedHandled));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new OrderCreatedEvent("order-1"));

        derivedHandled.Should().ContainSingle("derived handler should be called");
        baseHandled.Should().BeEmpty("base handler should NOT be called without polymorphic dispatch");
    }

    [Fact]
    public async Task Should_Handle_Three_Level_Hierarchy()
    {
        var baseHandled = new List<string>();
        var midHandled = new List<string>();
        var leafHandled = new List<string>();

        var services = new ServiceCollection();
        services.AddMediant(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PolymorphicNotificationTests).Assembly);
            cfg.EnablePolymorphicNotifications = true;
        });
        services.AddSingleton<INotificationHandler<BaseOrderEvent>>(
            new TrackingHandler<BaseOrderEvent>(baseHandled));
        services.AddSingleton<INotificationHandler<OrderCreatedEvent>>(
            new TrackingHandler<OrderCreatedEvent>(midHandled));
        services.AddSingleton<INotificationHandler<PriorityOrderCreatedEvent>>(
            new TrackingHandler<PriorityOrderCreatedEvent>(leafHandled));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new PriorityOrderCreatedEvent("order-1", "high"));

        leafHandled.Should().ContainSingle();
        midHandled.Should().ContainSingle();
        baseHandled.Should().ContainSingle();
    }

    [Fact]
    public async Task Should_Not_Duplicate_When_Publishing_Base_Type_Directly()
    {
        var baseHandled = new List<string>();

        var services = new ServiceCollection();
        services.AddMediant(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(PolymorphicNotificationTests).Assembly);
            cfg.EnablePolymorphicNotifications = true;
        });
        services.AddSingleton<INotificationHandler<BaseOrderEvent>>(
            new TrackingHandler<BaseOrderEvent>(baseHandled));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new BaseOrderEvent("order-1"));

        baseHandled.Should().ContainSingle("base handler should only be called once");
    }
}

// Test notification hierarchy
public record BaseOrderEvent(string OrderId) : INotification;
public record OrderCreatedEvent(string OrderId) : BaseOrderEvent(OrderId);
public record PriorityOrderCreatedEvent(string OrderId, string Priority) : OrderCreatedEvent(OrderId);

internal sealed class TrackingHandler<T> : INotificationHandler<T> where T : INotification
{
    private readonly List<string> _tracked;

    public TrackingHandler(List<string> tracked) => _tracked = tracked;

    public ValueTask Handle(T notification, CancellationToken cancellationToken)
    {
        _tracked.Add(typeof(T).Name);
        return ValueTask.CompletedTask;
    }
}
