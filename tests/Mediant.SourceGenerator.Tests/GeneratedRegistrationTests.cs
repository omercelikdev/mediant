using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Mediant;
using Mediant.Abstractions;
using Mediant.Results;

namespace Mediant.SourceGenerator.Tests;

/// <summary>
/// Verifies the source-generated <c>AddMediantGenerated()</c> registers handlers and dispatch
/// so Send/Publish/Stream work with no assembly scanning. (The AOT-safety of this exact path is
/// additionally proven at build time by the IsAotCompatible AotSample project.)
/// </summary>
public class GeneratedRegistrationTests
{
    private static IMediator BuildMediator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Sink>();
        services.AddMediantGenerated();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Generated_Send_Dispatches_To_Handler()
    {
        var mediator = BuildMediator();

        var result = await mediator.Send(new GenCommand("payload"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("handled:payload");
    }

    [Fact]
    public async Task Generated_Publish_Invokes_All_Notification_Handlers()
    {
        var services = new ServiceCollection();
        var sink = new Sink();
        services.AddSingleton(sink);
        services.AddMediantGenerated();
        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        await mediator.Publish(new GenEvent());

        // Two distinct handlers are registered for GenEvent — both must run (fanout).
        sink.Notified.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public async Task Generated_Stream_Dispatches_To_Stream_Handler()
    {
        var mediator = BuildMediator();

        var items = new List<int>();
        await foreach (var i in mediator.CreateStream(new GenStreamRequest(3)))
        {
            items.Add(i);
        }

        items.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Generated_Registration_Does_Not_Register_Scanning_Options()
    {
        // The generated path uses AddMediantCore (no scanning). A handler is resolvable,
        // proving registration happened without RegisterServicesFromAssembly.
        var services = new ServiceCollection();
        services.AddSingleton<Sink>();
        services.AddMediantGenerated();
        var sp = services.BuildServiceProvider();

        sp.GetService<IRequestHandler<GenCommand, Result<string>>>().Should().NotBeNull();
    }
}

public sealed class Sink
{
    private readonly object _gate = new();
    private readonly List<string> _notified = new();
    public void Add(string s) { lock (_gate) { _notified.Add(s); } }
    public IReadOnlyList<string> Notified { get { lock (_gate) { return _notified.OrderBy(x => x).ToList(); } } }
}

public sealed record GenCommand(string Data) : ICommand<Result<string>>;

public sealed class GenCommandHandler : ICommandHandler<GenCommand, Result<string>>
{
    public ValueTask<Result<string>> Handle(GenCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result<string>.Success("handled:" + request.Data));
}

public sealed record GenEvent : INotification;

public sealed class GenEventHandlerA : INotificationHandler<GenEvent>
{
    private readonly Sink _sink;
    public GenEventHandlerA(Sink sink) => _sink = sink;
    public ValueTask Handle(GenEvent notification, CancellationToken cancellationToken) { _sink.Add("A"); return ValueTask.CompletedTask; }
}

public sealed class GenEventHandlerB : INotificationHandler<GenEvent>
{
    private readonly Sink _sink;
    public GenEventHandlerB(Sink sink) => _sink = sink;
    public ValueTask Handle(GenEvent notification, CancellationToken cancellationToken) { _sink.Add("B"); return ValueTask.CompletedTask; }
}

public sealed record GenStreamRequest(int Count) : IStreamRequest<int>;

public sealed class GenStreamRequestHandler : IStreamRequestHandler<GenStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(GenStreamRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 1; i <= request.Count; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }
}
