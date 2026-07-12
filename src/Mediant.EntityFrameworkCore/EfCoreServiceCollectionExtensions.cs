using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mediant.Abstractions;

namespace Mediant.EntityFrameworkCore;

/// <summary>
/// DI extensions for registering the EF Core durable stores.
/// </summary>
public static class EfCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EfOutboxStore{TContext}"/> as the <see cref="IOutboxStore"/>. Map the
    /// outbox entity with <c>ModelBuilder.ConfigureMediantOutbox()</c> in your context.
    /// </summary>
    public static IServiceCollection AddMediantEfCoreOutboxStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IOutboxStore, EfOutboxStore<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfAuditStore{TContext}"/> as the <see cref="IAuditStore"/>. Map the
    /// audit entity with <c>ModelBuilder.ConfigureMediantAudit()</c> in your context.
    /// </summary>
    public static IServiceCollection AddMediantEfCoreAuditStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditStore, EfAuditStore<TContext>>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfCoreUnitOfWork{TContext}"/> as the <see cref="IUnitOfWork"/> so
    /// <c>[Transactional]</c> works against <typeparamref name="TContext"/> out of the box.
    /// Register alongside <c>AddMediantTransactions()</c>. Because it resolves the same scoped
    /// context as your handlers (and <see cref="EfOutboxStore{TContext}"/>), business writes and
    /// outbox messages commit atomically.
    /// </summary>
    public static IServiceCollection AddMediantEfCoreUnitOfWork<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IUnitOfWork, EfCoreUnitOfWork<TContext>>();
        return services;
    }
}
