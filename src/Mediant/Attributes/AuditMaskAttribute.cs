namespace Mediant.Attributes;

/// <summary>
/// Marks a property to be masked in audit entries with a custom mask pattern.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class AuditMaskAttribute : Attribute
{
    /// <summary>
    /// Gets the mask pattern to use. Defaults to "***".
    /// </summary>
    public string MaskPattern { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AuditMaskAttribute"/>.
    /// </summary>
    /// <param name="maskPattern">The mask pattern to use.</param>
    public AuditMaskAttribute(string maskPattern = "***")
    {
        MaskPattern = maskPattern;
    }
}
