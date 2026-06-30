using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.EntityFrameworkCore;

/// <summary>
/// DI extensions for registering the EF Core durable stores.
/// </summary>
public static class EfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfOutboxStore{TContext}"/> as the <see cref="IOutboxStore"/>. Map the
    /// outbox entity with <c>ModelBuilder.ConfigureQorpeOutbox()</c> in your context.
    /// </summary>
    public static IServiceCollection AddQorpeEfCoreOutboxStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IOutboxStore, EfOutboxStore<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfAuditStore{TContext}"/> as the <see cref="IAuditStore"/>. Map the
    /// audit entity with <c>ModelBuilder.ConfigureQorpeAudit()</c> in your context.
    /// </summary>
    public static IServiceCollection AddQorpeEfCoreAuditStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditStore, EfAuditStore<TContext>>();
        return services;
    }
}
