using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qorpe.Mediator.Abstractions;
using Qorpe.Mediator.Audit;
using Qorpe.Mediator.Behaviors.Behaviors;
using Qorpe.Mediator.Behaviors.Configuration;
using Qorpe.Mediator.Behaviors.UserContext;

namespace Qorpe.Mediator.Behaviors.DependencyInjection;

/// <summary>
/// Extension methods for registering Qorpe.Mediator behavior services.
/// </summary>
public static class BehaviorServiceCollectionExtensions
{
    /// <summary>
    /// Adds audit behavior.
    /// </summary>
    public static IServiceCollection AddQorpeAuditing(
        this IServiceCollection services,
        Action<AuditBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<AuditBehaviorOptions>(_ => { });

        services.TryAddSingleton<IAuditStore>(NullAuditStore.Instance);
        services.TryAddSingleton<IAuditUserContext, SystemUserContextProvider>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds logging behavior.
    /// </summary>
    public static IServiceCollection AddQorpeLogging(
        this IServiceCollection services,
        Action<LoggingBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<LoggingBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<LoggingBehaviorOptions>()
            .Validate(o => o.MaxSerializedLength > 0, "MaxSerializedLength must be positive.");

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds unhandled exception behavior.
    /// </summary>
    public static IServiceCollection AddQorpeUnhandledExceptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds authorization behavior.
    /// </summary>
    public static IServiceCollection AddQorpeAuthorization(
        this IServiceCollection services,
        Action<AuthorizationBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<AuthorizationBehaviorOptions>(_ => { });

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds transaction behavior with optional post-commit task queue.
    /// </summary>
    public static IServiceCollection AddQorpeTransactions(
        this IServiceCollection services,
        Action<TransactionBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<TransactionBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<TransactionBehaviorOptions>()
            .Validate(o => o.DefaultTimeoutSeconds > 0, "DefaultTimeoutSeconds must be positive.");

        services.TryAddScoped<IPostCommitTaskQueue, Behaviors.PostCommitTaskQueue>();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds idempotency behavior.
    /// </summary>
    public static IServiceCollection AddQorpeIdempotency(
        this IServiceCollection services,
        Action<IdempotencyBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<IdempotencyBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<IdempotencyBehaviorOptions>()
            .Validate(o => o.DefaultWindowSeconds > 0, "DefaultWindowSeconds must be positive.");

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers a production <see cref="IIdempotencyStore"/> backed by <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>.
    /// Call this after registering a distributed cache (e.g. <c>AddStackExchangeRedisCache</c> or
    /// <c>AddDistributedMemoryCache</c>) so <c>[Idempotent]</c> works out of the box.
    /// </summary>
    public static IServiceCollection AddQorpeDistributedCacheIdempotencyStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IIdempotencyStore, Idempotency.DistributedCacheIdempotencyStore>();
        return services;
    }

    /// <summary>
    /// Adds performance monitoring behavior.
    /// </summary>
    public static IServiceCollection AddQorpePerformanceMonitoring(
        this IServiceCollection services,
        Action<PerformanceBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<PerformanceBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<PerformanceBehaviorOptions>()
            .Validate(o => o.WarningThresholdMs > 0, "WarningThresholdMs must be positive.")
            .Validate(o => o.CriticalThresholdMs > 0, "CriticalThresholdMs must be positive.")
            .Validate(o => o.CriticalThresholdMs > o.WarningThresholdMs, "CriticalThresholdMs must be greater than WarningThresholdMs.");

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds retry behavior.
    /// </summary>
    public static IServiceCollection AddQorpeRetry(
        this IServiceCollection services,
        Action<RetryBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<RetryBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<RetryBehaviorOptions>()
            .Validate(o => o.DefaultMaxRetryCount >= 0, "DefaultMaxRetryCount must be non-negative.")
            .Validate(o => o.DefaultInitialDelayMs > 0, "DefaultInitialDelayMs must be positive.")
            .Validate(o => o.MaxBackoffCapSeconds > 0, "MaxBackoffCapSeconds must be positive.");

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds caching behavior.
    /// </summary>
    public static IServiceCollection AddQorpeCaching(
        this IServiceCollection services,
        Action<CachingBehaviorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (configure is not null) services.Configure(configure);
        else services.Configure<CachingBehaviorOptions>(_ => { });

        services.AddOptionsWithValidateOnStart<CachingBehaviorOptions>()
            .Validate(o => o.DefaultDurationSeconds > 0, "DefaultDurationSeconds must be positive.")
            .Validate(o => o.MaxLockPoolSize > 0, "MaxLockPoolSize must be positive.");

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Adds all behaviors in recommended pipeline order.
    /// </summary>
    public static IServiceCollection AddQorpeAllBehaviors(
        this IServiceCollection services,
        Action<AllBehaviorsOptions>? configure = null)
    {
        var opts = new AllBehaviorsOptions();
        configure?.Invoke(opts);

        // Pipeline order: Audit, Logging, UnhandledException, Authorization,
        // Validation (separate package), Idempotency, Transaction, Performance, Retry, Caching
        services.AddQorpeAuditing(opts.ConfigureAudit);
        services.AddQorpeLogging(opts.ConfigureLogging);
        services.AddQorpeUnhandledExceptions();
        services.AddQorpeAuthorization(opts.ConfigureAuthorization);
        services.AddQorpeIdempotency(opts.ConfigureIdempotency);
        services.AddQorpeTransactions(opts.ConfigureTransactions);
        services.AddQorpePerformanceMonitoring(opts.ConfigurePerformance);
        services.AddQorpeRetry(opts.ConfigureRetry);
        services.AddQorpeCaching(opts.ConfigureCaching);

        return services;
    }
}

/// <summary>
/// Aggregated configuration for all behaviors.
/// </summary>
public sealed class AllBehaviorsOptions
{
    public Action<AuditBehaviorOptions>? ConfigureAudit { get; set; }
    public Action<LoggingBehaviorOptions>? ConfigureLogging { get; set; }
    public Action<AuthorizationBehaviorOptions>? ConfigureAuthorization { get; set; }
    public Action<TransactionBehaviorOptions>? ConfigureTransactions { get; set; }
    public Action<IdempotencyBehaviorOptions>? ConfigureIdempotency { get; set; }
    public Action<PerformanceBehaviorOptions>? ConfigurePerformance { get; set; }
    public Action<RetryBehaviorOptions>? ConfigureRetry { get; set; }
    public Action<CachingBehaviorOptions>? ConfigureCaching { get; set; }
}
