using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.DependencyInjection;
using Mediant.Behaviors.Outbox;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Behaviors;

public class OutboxTests
{
    [Fact]
    public async Task InMemoryStore_RoundTrips_And_Marks_State()
    {
        var store = new InMemoryOutboxStore();
        var msg = new OutboxMessage { Id = Guid.NewGuid(), NotificationType = "T", Payload = "{}", OccurredOn = DateTimeOffset.UtcNow };

        await store.AddAsync(msg, default);
        (await store.GetUnprocessedAsync(10, default)).Should().ContainSingle();

        await store.MarkProcessedAsync(msg.Id, DateTimeOffset.UtcNow, default);
        (await store.GetUnprocessedAsync(10, default)).Should().BeEmpty();

        var msg2 = new OutboxMessage { Id = Guid.NewGuid(), NotificationType = "T", Payload = "{}", OccurredOn = DateTimeOffset.UtcNow };
        await store.AddAsync(msg2, default);
        await store.MarkFailedAsync(msg2.Id, attempts: 3, error: "boom", default);
        store.GetAll().Single(m => m.Id == msg2.Id).Attempts.Should().Be(3);
    }

    [Fact]
    public async Task Enqueue_Persists_Serialized_Notification()
    {
        var store = new InMemoryOutboxStore();
        var outbox = new DefaultOutbox(store);

        await outbox.EnqueueAsync(new OutboxEvent("hello"));

        var stored = store.GetAll().Should().ContainSingle().Subject;
        stored.NotificationType.Should().Contain(nameof(OutboxEvent));
        stored.Payload.Should().Contain("hello");
        stored.ProcessedOn.Should().BeNull();
    }

    [Fact]
    public async Task Enqueue_Honors_Custom_SerializerOptions()
    {
        var store = new InMemoryOutboxStore();
        var options = Options.Create(new OutboxProcessorOptions
        {
            SerializerOptions = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            },
        });
        var outbox = new DefaultOutbox(store, options);

        await outbox.EnqueueAsync(new OutboxEvent("hi"));

        // Custom options (camelCase) must be used for the payload.
        var payload = store.GetAll().Single().Payload;
        payload.Should().Contain("\"message\"").And.NotContain("\"Message\"");
    }

    [Fact]
    public async Task Processor_Publishes_Enqueued_Notification_And_Marks_Processed()
    {
        var sink = new OutboxSink();
        var sp = BuildProvider(sink);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new OutboxEvent("reliable"));
        }

        await NewProcessor(sp).ProcessPendingAsync(default);

        // The handler ran via the mediator, and the message is marked processed.
        sink.Received.Should().ContainSingle().Which.Should().Be("reliable");
        var store = (InMemoryOutboxStore)sp.GetRequiredService<IOutboxStore>();
        store.GetAll().Should().ContainSingle().Which.ProcessedOn.Should().NotBeNull();
    }

    [Fact]
    public async Task Processor_Records_Failure_And_Does_Not_Mark_Processed_When_Handler_Throws()
    {
        var sink = new OutboxSink();
        var sp = BuildProvider(sink);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new FailingOutboxEvent());
        }

        await NewProcessor(sp).ProcessPendingAsync(default);

        var store = (InMemoryOutboxStore)sp.GetRequiredService<IOutboxStore>();
        var message = store.GetAll().Should().ContainSingle().Subject;
        message.ProcessedOn.Should().BeNull("a failed dispatch must remain pending for retry");
        message.Attempts.Should().Be(1);
        message.Error.Should().NotBeNullOrEmpty();
    }

    private static ServiceProvider BuildProvider(OutboxSink sink)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(sink);
        services.AddQorpeMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(OutboxTests).Assembly));
        services.AddQorpeOutbox();
        return services.BuildServiceProvider();
    }

    private static OutboxProcessor NewProcessor(IServiceProvider sp)
        => new(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptions<OutboxProcessorOptions>>(),
            sp.GetRequiredService<ILogger<OutboxProcessor>>());
}

public sealed class OutboxSink
{
    private readonly object _gate = new();
    private readonly List<string> _received = new();
    public void Add(string s) { lock (_gate) { _received.Add(s); } }
    public IReadOnlyList<string> Received { get { lock (_gate) { return _received.ToList(); } } }
}

public sealed record OutboxEvent(string Message) : INotification;

internal sealed class OutboxEventHandler : INotificationHandler<OutboxEvent>
{
    private readonly OutboxSink _sink;
    public OutboxEventHandler(OutboxSink sink) => _sink = sink;
    public ValueTask Handle(OutboxEvent notification, CancellationToken cancellationToken)
    {
        _sink.Add(notification.Message);
        return ValueTask.CompletedTask;
    }
}

public sealed record FailingOutboxEvent : INotification;

internal sealed class FailingOutboxEventHandler : INotificationHandler<FailingOutboxEvent>
{
    public ValueTask Handle(FailingOutboxEvent notification, CancellationToken cancellationToken)
        => throw new InvalidOperationException("outbox handler failure");
}
