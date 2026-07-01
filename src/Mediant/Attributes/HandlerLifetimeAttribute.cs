using Microsoft.Extensions.DependencyInjection;

namespace Mediant.Attributes;

/// <summary>
/// Overrides the global handler lifetime for a specific handler implementation.
/// When applied, the handler is registered with the specified lifetime instead
/// of <see cref="DependencyInjection.MediatorOptions.HandlerLifetime"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HandlerLifetimeAttribute : Attribute
{
    /// <summary>
    /// Gets the service lifetime for this handler.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HandlerLifetimeAttribute"/>.
    /// </summary>
    /// <param name="lifetime">The service lifetime to use for this handler.</param>
    public HandlerLifetimeAttribute(ServiceLifetime lifetime)
    {
        Lifetime = lifetime;
    }
}
