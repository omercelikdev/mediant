using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Caching;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;

namespace Mediant.UnitTests.Behaviors;

/// <summary>
/// HybridCache-backed query caching path (#130): L1+L2 with built-in stampede protection and
/// tag-based invalidation. Registered via <c>AddMediantHybridCaching</c>.
/// </summary>
public class HybridCachingTests
{
    [Fact]
    public async Task Query_Is_Served_From_Cache_On_Second_Call()
    {
        var executions = 0;
        var sender = BuildProvider(() => executions++).GetRequiredService<ISender>();

        await sender.Send(new HybridRatesQuery());
        await sender.Send(new HybridRatesQuery());

        executions.Should().Be(1, "HybridCache must serve the second query from cache");
    }

    [Fact]
    public async Task InvalidatesCache_Command_Evicts_Via_Tag()
    {
        var executions = 0;
        var sender = BuildProvider(() => executions++).GetRequiredService<ISender>();

        await sender.Send(new HybridRatesQuery());   // caches under tag "hybrid-rates"
        await sender.Send(new HybridRatesQuery());   // served from cache
        executions.Should().Be(1);

        await sender.Send(new UpdateHybridRatesCommand()); // [InvalidatesCache("hybrid-rates")] → RemoveByTag
        await sender.Send(new HybridRatesQuery());          // re-executes

        executions.Should().Be(2, "[InvalidatesCache] must RemoveByTag so the query re-runs");
    }

    [Fact]
    public async Task InvalidateAll_Evicts_Every_Cached_Query()
    {
        var executions = 0;
        var sp = BuildProvider(() => executions++);
        var sender = sp.GetRequiredService<ISender>();

        await sender.Send(new HybridRatesQuery());
        executions.Should().Be(1);

        await sp.GetRequiredService<ICacheInvalidator>().InvalidateAllAsync();
        await sender.Send(new HybridRatesQuery());

        executions.Should().Be(2, "InvalidateAllAsync must clear the all-entries tag");
    }

    [Fact]
    public void AddMediantHybridCaching_Registers_HybridCacheInvalidator()
    {
        var sp = BuildProvider(() => { });
        sp.GetService<ICacheInvalidator>().Should().BeOfType<HybridCacheInvalidator>();
    }

    private static ServiceProvider BuildProvider(Action onQueryExecute)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        services.AddSingleton(onQueryExecute);
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(HybridCachingTests).Assembly));
        services.AddMediantHybridCaching();
        return services.BuildServiceProvider();
    }
}

[Cacheable(300, CacheKeyPrefix = "hybrid-rates")]
public sealed record HybridRatesQuery : IQuery<string>;

public sealed class HybridRatesQueryHandler : IQueryHandler<HybridRatesQuery, string>
{
    private readonly Action _onExecute;
    public HybridRatesQueryHandler(Action onExecute) => _onExecute = onExecute;

    public ValueTask<string> Handle(HybridRatesQuery request, CancellationToken cancellationToken)
    {
        _onExecute();
        return new ValueTask<string>("hybrid-rate-data");
    }
}

[InvalidatesCache("hybrid-rates")]
public sealed record UpdateHybridRatesCommand : ICommand<string>;

public sealed class UpdateHybridRatesCommandHandler : ICommandHandler<UpdateHybridRatesCommand, string>
{
    public ValueTask<string> Handle(UpdateHybridRatesCommand request, CancellationToken cancellationToken)
        => new("updated");
}
