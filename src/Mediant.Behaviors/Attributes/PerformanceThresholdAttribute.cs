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

    /// <summary>
    /// Gets or sets the hard-ceiling override in milliseconds for this request type; above it the
    /// request is always logged as Critical. When 0 (default) the global
    /// <c>PerformanceBehaviorOptions.HardCeilingMs</c> applies. Set a negative value to disable
    /// the ceiling for this request type (long-running by design, e.g. batch commands).
    /// </summary>
    public int CeilingMs { get; set; }
}
