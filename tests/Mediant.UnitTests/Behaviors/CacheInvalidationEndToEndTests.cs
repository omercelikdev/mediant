using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Behaviors;
using Mediant.Behaviors.Caching;
using Mediant.Behaviors.Configuration;
using Mediant.Behaviors.DependencyInjection;
using Mediant.DependencyInjection;
using Mediant.Results;

namespace Mediant.UnitTests.Behaviors;

/// <summary>
/// End-to-end coverage for the default cache invalidator shipped by <c>AddMediantCaching</c>
/// (#131 — previously <c>[InvalidatesCache]</c> was a silent no-op because no
/// <see cref="ICacheInvalidator"/> was registered).
/// </summary>
public class CacheInvalidationEndToEndTests
{
    [Fact]
    public async Task InvalidatesCache_Command_Actually_Evicts_Cached_Query()
    {
        var executions = 0;
        var sp = BuildProvider(() => executions++);
        var sender = sp.GetRequiredService<ISender>();

        // 1. Query executes the handler and caches under "rates:{hash}".
        await sender.Send(new GetRatesQuery());
        executions.Should().Be(1);

        // 2. Served from cache — handler not re-run.
        await sender.Send(new GetRatesQuery());
        executions.Should().Be(1);

        // 3. Command with [InvalidatesCache("rates")] must evict the entry.
        await sender.Send(new UpdateRatesCommand());

        // 4. Query re-executes the handler (cache was invalidated, not served stale).
        await sender.Send(new GetRatesQuery());
        executions.Should().Be(2, "[InvalidatesCache] must evict the cached entry so the query re-runs");
    }

    [Fact]
    public async Task Default_Invalidator_Is_Registered_By_AddMediantCaching()
    {
        var sp = BuildProvider(() => { });
        sp.GetService<ICacheInvalidator>().Should().BeOfType<DistributedCacheInvalidator>();
        sp.GetService<ICacheKeyRegistry>().Should().BeOfType<DistributedCacheKeyRegistry>();
    }

    [Fact]
    public async Task InvalidateAll_Evicts_Across_Prefixes()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        services.Configure<CachingBehaviorOptions>(_ => { });
        services.AddSingleton<ICacheKeyRegistry, DistributedCacheKeyRegistry>();
        var sp = services.BuildServiceProvider();

        var cache = sp.GetRequiredService<IDistributedCache>();
        var registry = sp.GetRequiredService<ICacheKeyRegistry>();
        await cache.SetStringAsync("a:1", "x");
        await cache.SetStringAsync("b:1", "y");
        await registry.RegisterAsync("a", "a:1", TimeSpan.FromMinutes(5), default);
        await registry.RegisterAsync("b", "b:1", TimeSpan.FromMinutes(5), default);

        var invalidator = new DistributedCacheInvalidator(registry, cache);
        await invalidator.InvalidateAllAsync();

        (await cache.GetAsync("a:1")).Should().BeNull();
        (await cache.GetAsync("b:1")).Should().BeNull();
    }

    [Fact]
    public async Task CacheInvalidationBehavior_Warns_Once_When_No_Invalidator()
    {
        var logger = Substitute.For<ILogger<CacheInvalidationBehavior<UpdateRatesCommand, string>>>();
        var behavior = new CacheInvalidationBehavior<UpdateRatesCommand, string>(logger, cacheInvalidator: null);

        RequestHandlerDelegate<string> next = () => new ValueTask<string>("ok");

        await behavior.Handle(new UpdateRatesCommand(), next, default);
        await behavior.Handle(new UpdateRatesCommand(), next, default);

        // Warned exactly once across the two invocations (static guard per closed generic type).
        logger.ReceivedCalls()
            .Count(c => c.GetArguments().OfType<LogLevel>().Contains(LogLevel.Warning))
            .Should().Be(1);
    }

    private static ServiceProvider BuildProvider(Action onQueryExecute)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDistributedMemoryCache();
        services.AddSingleton(onQueryExecute);
        services.AddMediant(cfg => cfg.RegisterServicesFromAssembly(typeof(CacheInvalidationEndToEndTests).Assembly));
        services.AddMediantCaching();
        return services.BuildServiceProvider();
    }
}

[Cacheable(300, CacheKeyPrefix = "rates")]
public sealed record GetRatesQuery : IQuery<string>;

public sealed class GetRatesQueryHandler : IQueryHandler<GetRatesQuery, string>
{
    private readonly Action _onExecute;
    public GetRatesQueryHandler(Action onExecute) => _onExecute = onExecute;

    public ValueTask<string> Handle(GetRatesQuery request, CancellationToken cancellationToken)
    {
        _onExecute();
        return new ValueTask<string>("rate-data");
    }
}

[InvalidatesCache("rates")]
public sealed record UpdateRatesCommand : ICommand<string>;

public sealed class UpdateRatesCommandHandler : ICommandHandler<UpdateRatesCommand, string>
{
    public ValueTask<string> Handle(UpdateRatesCommand request, CancellationToken cancellationToken)
        => new("updated");
}
