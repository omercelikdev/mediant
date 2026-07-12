using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;
using Mediant.Audit;
using Mediant.Behaviors.Audit;
using Mediant.Behaviors.DependencyInjection;
using Mediant.EntityFrameworkCore;

namespace Mediant.EntityFrameworkCore.Tests;

/// <summary>
/// Regression for the TryAdd registration-order trap (#139): the EF Core store extensions must
/// win over the library defaults (<c>NullAuditStore</c>, <c>InMemoryOutboxStore</c>) regardless
/// of whether they are called before or after the behavior registrations — previously the wrong
/// order silently kept the defaults and audit/outbox durability vanished without any error.
/// </summary>
public sealed class EfStoreRegistrationOrderTests
{
    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TestDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        return services;
    }

    [Fact]
    public void EfAuditStore_Wins_When_Registered_After_AddMediantAuditing()
    {
        var services = NewServices();
        services.AddMediantAuditing();                       // registers NullAuditStore default
        services.AddMediantEfCoreAuditStore<TestDbContext>(); // previously a silent no-op

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditStore>()
            .Should().BeOfType<EfAuditStore<TestDbContext>>();
    }

    [Fact]
    public void EfAuditStore_Wins_When_Registered_Before_AddMediantAuditing()
    {
        var services = NewServices();
        services.AddMediantEfCoreAuditStore<TestDbContext>();
        services.AddMediantAuditing();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditStore>()
            .Should().BeOfType<EfAuditStore<TestDbContext>>();
    }

    [Fact]
    public void EfOutboxStore_Wins_When_Registered_After_AddMediantOutbox()
    {
        var services = NewServices();
        services.AddMediantOutbox();                          // registers InMemoryOutboxStore default
        services.AddMediantEfCoreOutboxStore<TestDbContext>(); // previously a silent no-op

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .Should().BeOfType<EfOutboxStore<TestDbContext>>();
    }

    [Fact]
    public void EfOutboxStore_Wins_When_Registered_Before_AddMediantOutbox()
    {
        var services = NewServices();
        services.AddMediantEfCoreOutboxStore<TestDbContext>();
        services.AddMediantOutbox();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .Should().BeOfType<EfOutboxStore<TestDbContext>>();
    }

    [Fact]
    public void A_Custom_Store_Registered_After_The_Ef_Extension_Still_Wins()
    {
        // Standard last-wins DI semantics are preserved: the EF extension replaces what came
        // before it, it does not pin itself against later explicit registrations.
        var services = NewServices();
        services.AddMediantEfCoreAuditStore<TestDbContext>();
        services.AddScoped<IAuditStore, CustomAuditStore>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditStore>()
            .Should().BeOfType<CustomAuditStore>();
    }

    [Fact]
    public void Audit_Buffering_Still_Decorates_The_Ef_Store_In_The_Documented_Order()
    {
        // AddMediantAuditBuffering's documented order: register the durable store first, then
        // the buffering decorator. The Replace-based EF registration must not break it.
        var services = NewServices();
        services.AddMediantEfCoreAuditStore<TestDbContext>();
        services.AddMediantAuditBuffering<EfAuditStore<TestDbContext>>();
        services.AddMediantAuditing();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditStore>()
            .Should().BeOfType<BufferedAuditStore<EfAuditStore<TestDbContext>>>();
    }

    private sealed class CustomAuditStore : IAuditStore
    {
        public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<AuditEntry>>([]);
    }
}
