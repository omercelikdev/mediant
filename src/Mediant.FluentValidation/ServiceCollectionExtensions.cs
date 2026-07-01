using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Mediant.Abstractions;

namespace Mediant.FluentValidation;

/// <summary>
/// Extension methods for registering FluentValidation with Mediant.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FluentValidation integration for Mediant.
    /// Auto-discovers validators from registered assemblies.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Assemblies to scan for validators.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddQorpeValidation(
        this IServiceCollection services,
        params System.Reflection.Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // Register validators from assemblies
        services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Transient);

        // Register validation behavior
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
