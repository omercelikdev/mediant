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
    /// <para>
    /// Replaces any existing <see cref="IOutboxStore"/> registration, so it wins over the
    /// in-memory default regardless of whether it is called before or after
    /// <c>AddMediantOutbox()</c>. Standard last-wins DI semantics still apply to anything you
    /// register after this call.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMediantEfCoreOutboxStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Scoped<IOutboxStore, EfOutboxStore<TContext>>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="EfAuditStore{TContext}"/> as the <see cref="IAuditStore"/>. Map the
    /// audit entity with <c>ModelBuilder.ConfigureMediantAudit()</c> in your context.
    /// <para>
    /// Replaces any existing <see cref="IAuditStore"/> registration, so it wins over the null
    /// default regardless of whether it is called before or after <c>AddMediantAuditing()</c>.
    /// Standard last-wins DI semantics still apply to anything you register after this call —
    /// e.g. call <c>AddMediantAuditBuffering&lt;EfAuditStore&lt;TContext&gt;&gt;()</c> after this
    /// to decorate it with buffering.
    /// </para>
    /// </summary>
    public static IServiceCollection AddMediantEfCoreAuditStore<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Scoped<IAuditStore, EfAuditStore<TContext>>());
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
