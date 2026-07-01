using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;
using Mediant.Behaviors.DependencyInjection;
using Mediant.Behaviors.Idempotency;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

public class DistributedCacheIdempotencyStoreTests
{
    private static IDistributedCache NewCache()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        return services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    [Fact]
    public async Task Store_RoundTrips_Result_Value_And_Honors_Remove()
    {
        var store = new DistributedCacheIdempotencyStore(NewCache());
        var key = "idem-key-1";

        (await store.ExistsAsync(key, default)).Should().BeFalse();

        await store.SetAsync(key, Result<Guid>.Success(Guid.Parse("11111111-1111-1111-1111-111111111111")), TimeSpan.FromMinutes(5), default);

        (await store.ExistsAsync(key, default)).Should().BeTrue();
        var fetched = await store.GetAsync<Result<Guid>>(key, default);
        fetched!.IsSuccess.Should().BeTrue();
        fetched.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        await store.RemoveAsync(key, default);
        (await store.ExistsAsync(key, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Store_Honors_Custom_SerializerOptions()
    {
        // Custom (AOT-friendly) options must be accepted and round-trip the value.
        var options = Options.Create(new IdempotencyBehaviorOptions
        {
            SerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.General) { PropertyNamingPolicy = null },
        });
        var store = new DistributedCacheIdempotencyStore(NewCache(), options);

        await store.SetAsync("k2", Result<int>.Success(99), TimeSpan.FromMinutes(1), default);

        (await store.GetAsync<Result<int>>("k2", default))!.Value.Should().Be(99);
    }

    [Fact]
    public async Task Idempotent_Command_Sent_Twice_Executes_Handler_Once()
    {
        var counter = new ExecutionCounter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(counter);
        services.AddDistributedMemoryCache();
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(DistributedCacheIdempotencyStoreTests).Assembly));
        services.AddMediantIdempotency();
        services.AddMediantDistributedCacheIdempotencyStore();

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        var command = new IdempotentChargeCommand("order-1");

        var first = await mediator.Send(command);
        var second = await mediator.Send(command);

        counter.Count.Should().Be(1, "the handler must run only once for a repeated idempotent command");
        first.IsSuccess.Should().BeTrue();
        second.Value.Should().Be(first.Value, "the second call must return the cached result");
    }
}

public sealed class ExecutionCounter
{
    private int _count;
    public int Next() => Interlocked.Increment(ref _count);
    public int Count => Volatile.Read(ref _count);
}

[Idempotent(300)]
public sealed record IdempotentChargeCommand(string OrderId) : ICommand<Result<int>>;

internal sealed class IdempotentChargeHandler : ICommandHandler<IdempotentChargeCommand, Result<int>>
{
    private readonly ExecutionCounter _counter;
    public IdempotentChargeHandler(ExecutionCounter counter) => _counter = counter;

    public ValueTask<Result<int>> Handle(IdempotentChargeCommand request, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result<int>.Success(_counter.Next()));
}
