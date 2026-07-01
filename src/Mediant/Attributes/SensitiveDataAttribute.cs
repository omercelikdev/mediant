namespace Mediant.Attributes;

/// <summary>
/// Marks a property as containing sensitive data that should be masked in logs and audit entries.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class SensitiveDataAttribute : Attribute
{
}
