using Microsoft.Extensions.DependencyInjection;
using Mediant;
using Mediant.Abstractions;
using Mediant.AotSample;

// This program is built with IsAotCompatible=true. A clean build proves the source-generated
// registration and the mediator Send/Publish/Stream dispatch contain no AOT-incompatible calls.
var services = new ServiceCollection();
services.AddMediantGenerated();

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var sendResult = await mediator.Send(new PingCommand("hello"));
Console.WriteLine($"send: success={sendResult.IsSuccess} value={sendResult.Value}");

await mediator.Publish(new PingNotification("created"));
Console.WriteLine("publish: ok");

var items = new List<int>();
await foreach (var item in mediator.CreateStream(new NumbersRequest(3)))
{
    items.Add(item);
}
Console.WriteLine($"stream: {string.Join(",", items)}");

Console.WriteLine("AOT-SAMPLE-OK");
