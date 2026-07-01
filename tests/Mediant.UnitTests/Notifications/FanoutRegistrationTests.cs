using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Notifications;

/// <summary>
/// Regression tests for assembly-scan registration of multiple distinct handlers
/// for the same notification. Before the fix, registration used <c>TryAdd</c> which
/// keys only on the service type, silently dropping the second (and later) distinct
/// notification handler — breaking notification fanout via the primary registration path.
/// </summary>
public class FanoutRegistrationTests
{
    [Fact]
    public void Scan_Should_Register_All_Distinct_NotificationHandlers_For_Same_Event()
    {
        var sink = new HandlerSink();
        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FanoutRegistrationTests).Assembly));

        var sp = services.BuildServiceProvider();

        var handlers = sp.GetServices<INotificationHandler<FanoutEvent>>().ToList();

        handlers.Should().HaveCount(2,
            "both distinct notification handlers must be registered via assembly scanning");
    }

    [Fact]
    public async Task Publish_Should_Invoke_All_Distinct_NotificationHandlers_Registered_By_Scan()
    {
        var sink = new HandlerSink();
        var services = new ServiceCollection();
        services.AddSingleton(sink);
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FanoutRegistrationTests).Assembly));

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<IMediator>();

        await mediator.Publish(new FanoutEvent());

        sink.Invoked.Should().BeEquivalentTo(new[] { "Email", "Inventory" },
            "every distinct handler discovered by scanning must run on publish");
    }

    [Fact]
    public void Scan_Should_Still_Register_Exactly_One_RequestHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HandlerSink());
        services.AddMediant(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(FanoutRegistrationTests).Assembly));

        var sp = services.BuildServiceProvider();

        var handlers = sp.GetServices<IRequestHandler<FanoutQuery, int>>().ToList();
        handlers.Should().ContainSingle("request handlers remain single-instance");
    }
}

internal sealed class HandlerSink
{
    private readonly object _gate = new();
    private readonly List<string> _invoked = new();

    public void Record(string name)
    {
        lock (_gate)
        {
            _invoked.Add(name);
        }
    }

    public IReadOnlyList<string> Invoked
    {
        get { lock (_gate) { return _invoked.ToList(); } }
    }
}

public sealed record FanoutEvent : INotification;

internal sealed class FanoutEmailHandler : INotificationHandler<FanoutEvent>
{
    private readonly HandlerSink _sink;
    public FanoutEmailHandler(HandlerSink sink) => _sink = sink;
    public ValueTask Handle(FanoutEvent notification, CancellationToken cancellationToken)
    {
        _sink.Record("Email");
        return ValueTask.CompletedTask;
    }
}

internal sealed class FanoutInventoryHandler : INotificationHandler<FanoutEvent>
{
    private readonly HandlerSink _sink;
    public FanoutInventoryHandler(HandlerSink sink) => _sink = sink;
    public ValueTask Handle(FanoutEvent notification, CancellationToken cancellationToken)
    {
        _sink.Record("Inventory");
        return ValueTask.CompletedTask;
    }
}

public sealed record FanoutQuery : IQuery<int>;

internal sealed class FanoutQueryHandler : IQueryHandler<FanoutQuery, int>
{
    public ValueTask<int> Handle(FanoutQuery request, CancellationToken cancellationToken)
        => ValueTask.FromResult(42);
}
