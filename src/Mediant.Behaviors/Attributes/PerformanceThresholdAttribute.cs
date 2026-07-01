namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Overrides global performance monitoring thresholds for a specific request type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PerformanceThresholdAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the warning threshold in milliseconds.
    /// Requests exceeding this duration are logged at Warning level.
    /// </summary>
    public int WarningMs { get; set; }

    /// <summary>
    /// Gets or sets the critical threshold in milliseconds.
    /// Requests exceeding this duration are logged at Error level.
    /// </summary>
    public int CriticalMs { get; set; }
}
