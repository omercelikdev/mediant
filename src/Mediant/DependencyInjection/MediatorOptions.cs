using System.Reflection;
using Mediant.Abstractions;

namespace Mediant.DependencyInjection;

/// <summary>
/// Defines the strategy for publishing notifications.
/// </summary>
public enum NotificationPublishStrategy
{
    /// <summary>
    /// Executes handlers sequentially, stopping on first error.
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// Executes handlers sequentially, collecting all errors.
    /// </summary>
    SequentialContinueOnError = 1,

    /// <summary>
    /// Executes all handlers in parallel.
    /// </summary>
    Parallel = 2,

    /// <summary>
    /// Executes all handlers in parallel with a timeout.
    /// </summary>
    ParallelWithTimeout = 3
}

/// <summary>
/// Configuration options for the mediator.
/// </summary>
public sealed class MediatorOptions
{
    internal List<Assembly> AssembliesToRegister { get; } = new();
    internal List<(Type ServiceType, Type ImplementationType)> OpenBehaviors { get; } = new();

    /// <summary>
    /// Gets or sets the notification publish strategy.
    /// </summary>
    public NotificationPublishStrategy NotificationPublishStrategy { get; set; } = NotificationPublishStrategy.Sequential;

    /// <summary>
    /// Gets or sets the timeout for parallel notification publishing.
    /// Only used when <see cref="NotificationPublishStrategy"/> is <see cref="NotificationPublishStrategy.ParallelWithTimeout"/>.
    /// </summary>
    public TimeSpan ParallelTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to enable polymorphic notification dispatch.
    /// When enabled, publishing a derived notification also invokes handlers
    /// registered for base notification types. Default is false.
    /// </summary>
    public bool EnablePolymorphicNotifications { get; set; }

    /// <summary>
    /// Gets or sets whether to validate handler registrations at startup.
    /// When enabled, verifies that every discovered request type has a corresponding handler.
    /// Throws an exception listing all missing handlers if any are found. Default is false.
    /// </summary>
    public bool ValidateOnStartup { get; set; }

    /// <summary>
    /// Gets or sets the service lifetime for handlers. Defaults to Transient.
    /// </summary>
    public Microsoft.Extensions.DependencyInjection.ServiceLifetime HandlerLifetime { get; set; } =
        Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;

    /// <summary>
    /// Registers services from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>This options instance for chaining.</returns>
    public MediatorOptions RegisterServicesFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        AssembliesToRegister.Add(assembly);
        return this;
    }

    /// <summary>
    /// Registers services from multiple assemblies.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>This options instance for chaining.</returns>
    public MediatorOptions RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        AssembliesToRegister.AddRange(assemblies);
        return this;
    }

    /// <summary>
    /// Registers an open-generic pipeline behavior that applies to every request
    /// (e.g. <c>typeof(LoggingBehavior&lt;,&gt;)</c> implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>).
    /// Behaviors are added as a multi-instance registration, so several may be registered and run
    /// in <see cref="IBehaviorOrder"/> order.
    /// </summary>
    /// <param name="openBehaviorType">An open generic type definition implementing <see cref="IPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <returns>This options instance for chaining.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Inspects the behavior type's interfaces via reflection; not trimming-safe.")]
    public MediatorOptions AddOpenBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        AddOpenBehaviorCore(openBehaviorType, typeof(IPipelineBehavior<,>), "pipeline");
        return this;
    }

    /// <summary>
    /// Registers an open-generic stream pipeline behavior that applies to every stream request
    /// (an open generic type definition implementing <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/>).
    /// </summary>
    /// <param name="openBehaviorType">An open generic type definition implementing <see cref="IStreamPipelineBehavior{TRequest,TResponse}"/>.</param>
    /// <returns>This options instance for chaining.</returns>
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Inspects the behavior type's interfaces via reflection; not trimming-safe.")]
    public MediatorOptions AddOpenStreamBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);
        AddOpenBehaviorCore(openBehaviorType, typeof(IStreamPipelineBehavior<,>), "stream pipeline");
        return this;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Inspects the behavior type's interfaces via reflection; not trimming-safe.")]
    private void AddOpenBehaviorCore(Type openBehaviorType, Type openServiceType, string kind)
    {
        if (!openBehaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"'{openBehaviorType}' must be an open generic type definition, e.g. typeof(MyBehavior<,>).",
                nameof(openBehaviorType));
        }

        var implementsService = openBehaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openServiceType);

        if (!implementsService)
        {
            throw new ArgumentException(
                $"'{openBehaviorType}' must implement the open generic {kind} behavior interface '{openServiceType}'.",
                nameof(openBehaviorType));
        }

        OpenBehaviors.Add((openServiceType, openBehaviorType));
    }
}
