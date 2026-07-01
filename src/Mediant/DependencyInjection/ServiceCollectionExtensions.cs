using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mediant.Abstractions;
using Mediant.Attributes;
using Mediant.Implementation;

namespace Mediant.DependencyInjection;

/// <summary>
/// Extension methods for registering Mediant services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ScanAotMessage =
        "Assembly scanning uses reflection and is not trimming/Native-AOT safe. Under trimming/AOT, " +
        "register handlers with the source generator via services.AddMediantGenerated().";

    /// <summary>
    /// Registers the core mediator services (publisher, options, mediator) WITHOUT scanning.
    /// Trimming/Native-AOT safe. Used by the source-generated registration.
    /// </summary>
    public static IServiceCollection AddMediantCore(this IServiceCollection services, MediatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        RegisterNotificationPublisher(services, options);
        services.TryAddSingleton(options);
        services.TryAddSingleton<PipelineProbeCache>();
        services.TryAddTransient<IMediator, Implementation.Mediator>();
        services.TryAddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());
        return services;
    }

    /// <summary>
    /// Adds Mediant services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    [RequiresUnreferencedCode(ScanAotMessage)]
    [RequiresDynamicCode(ScanAotMessage)]
    public static IServiceCollection AddMediant(
        this IServiceCollection services,
        Action<MediatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MediatorOptions();
        configure(options);

        return AddMediantInternal(services, options);
    }

    /// <summary>
    /// Adds Mediant services to the service collection with default options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <returns>The service collection for chaining.</returns>
    [RequiresUnreferencedCode(ScanAotMessage)]
    [RequiresDynamicCode(ScanAotMessage)]
    public static IServiceCollection AddMediant(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        var options = new MediatorOptions();
        options.RegisterServicesFromAssemblies(assemblies);

        return AddMediantInternal(services, options);
    }

    [RequiresUnreferencedCode(ScanAotMessage)]
    [RequiresDynamicCode(ScanAotMessage)]
    private static IServiceCollection AddMediantInternal(
        IServiceCollection services,
        MediatorOptions options)
    {
        // Core (AOT-safe) services.
        AddMediantCore(services, options);

        // Scan assemblies and register handlers
        if (options.AssembliesToRegister.Count > 0)
        {
            var registrations = AssemblyScanner.Scan(options.AssembliesToRegister);

            for (int i = 0; i < registrations.Count; i++)
            {
                var reg = registrations[i];
                // Check for per-handler lifetime override via [HandlerLifetime] attribute
                var lifetimeAttr = reg.ImplementationType.GetCustomAttribute<HandlerLifetimeAttribute>();
                var lifetime = lifetimeAttr?.Lifetime ?? options.HandlerLifetime;
                var descriptor = new ServiceDescriptor(reg.ServiceType, reg.ImplementationType, lifetime);

                // Multi-instance service types (notification handlers, behaviors, pre/post processors)
                // can have many implementations per service type — they MUST be registered with
                // TryAddEnumerable so a second distinct implementation is not silently dropped.
                // Single-handler service types use TryAdd (one implementation per request type).
                if (IsMultiInstanceServiceType(reg.ServiceType))
                {
                    services.TryAddEnumerable(descriptor);
                }
                else
                {
                    services.TryAdd(descriptor);
                }
            }

            if (options.ValidateOnStartup)
            {
                ValidateHandlerRegistrations(options.AssembliesToRegister, registrations);
            }
        }

        // Register explicitly-added open-generic behaviors (multi-instance, so several can run).
        for (int i = 0; i < options.OpenBehaviors.Count; i++)
        {
            var (serviceType, implementationType) = options.OpenBehaviors[i];
            services.TryAddEnumerable(ServiceDescriptor.Transient(serviceType, implementationType));
        }

        return services;
    }

    // Service types that allow multiple implementations per closed generic type.
    // These must be registered with TryAddEnumerable so the DI container resolves
    // IEnumerable<T> to ALL registered implementations rather than just the first.
    private static bool IsMultiInstanceServiceType(Type serviceType)
    {
        if (!serviceType.IsGenericType)
        {
            return false;
        }

        var genericDef = serviceType.GetGenericTypeDefinition();
        // Single-instance: exactly one handler per request type.
        if (genericDef == typeof(IRequestHandler<,>) || genericDef == typeof(IStreamRequestHandler<,>))
        {
            return false;
        }

        // Everything else discovered by the scanner is multi-instance:
        // INotificationHandler<>, IPipelineBehavior<,>, IStreamPipelineBehavior<,>,
        // IRequestPreProcessor<>, IRequestPostProcessor<,>.
        return true;
    }

    private static void RegisterNotificationPublisher(
        IServiceCollection services,
        MediatorOptions options)
    {
        switch (options.NotificationPublishStrategy)
        {
            case NotificationPublishStrategy.Sequential:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: true));
                break;
            case NotificationPublishStrategy.SequentialContinueOnError:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: false));
                break;
            case NotificationPublishStrategy.Parallel:
                services.TryAddSingleton<INotificationPublisher>(new ParallelNotificationPublisher());
                break;
            case NotificationPublishStrategy.ParallelWithTimeout:
                services.TryAddSingleton<INotificationPublisher>(new ParallelNotificationPublisher(options.ParallelTimeout));
                break;
            default:
                services.TryAddSingleton<INotificationPublisher>(new ForeachNotificationPublisher(stopOnFirstError: true));
                break;
        }
    }

    [RequiresUnreferencedCode(ScanAotMessage)]
    [RequiresDynamicCode(ScanAotMessage)]
    private static void ValidateHandlerRegistrations(
        List<System.Reflection.Assembly> assemblies,
        IReadOnlyList<HandlerRegistration> registrations)
    {
        // Build set of registered handler service types
        var registeredHandlerTypes = new HashSet<Type>();
        for (int i = 0; i < registrations.Count; i++)
        {
            var reg = registrations[i];
            if (reg.ServiceType.IsGenericType)
            {
                var genericDef = reg.ServiceType.GetGenericTypeDefinition();
                if (genericDef == typeof(IRequestHandler<,>) || genericDef == typeof(IStreamRequestHandler<,>))
                {
                    registeredHandlerTypes.Add(reg.ServiceType);
                }
            }
        }

        // Scan for all request types and verify handlers exist
        var missingHandlers = new List<Type>();

        for (int i = 0; i < assemblies.Count; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            for (int j = 0; j < types.Length; j++)
            {
                var type = types[j];
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                    continue;

                var interfaces = type.GetInterfaces();
                for (int k = 0; k < interfaces.Length; k++)
                {
                    var iface = interfaces[k];
                    if (!iface.IsGenericType) continue;

                    var genericDef = iface.GetGenericTypeDefinition();

                    if (genericDef == typeof(IRequest<>))
                    {
                        var responseType = iface.GetGenericArguments()[0];
                        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(type, responseType);
                        if (!registeredHandlerTypes.Contains(handlerType))
                        {
                            missingHandlers.Add(type);
                        }
                    }
                    else if (genericDef == typeof(IStreamRequest<>))
                    {
                        var responseType = iface.GetGenericArguments()[0];
                        var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(type, responseType);
                        if (!registeredHandlerTypes.Contains(handlerType))
                        {
                            missingHandlers.Add(type);
                        }
                    }
                }
            }
        }

        if (missingHandlers.Count > 0)
        {
            var typeNames = string.Join(", ", missingHandlers.Select(t => t.Name));
            throw new InvalidOperationException(
                $"Handler registration validation failed. {missingHandlers.Count} request type(s) have no registered handler: {typeNames}. " +
                $"Create handler implementations or disable validation with ValidateOnStartup = false.");
        }
    }
}
