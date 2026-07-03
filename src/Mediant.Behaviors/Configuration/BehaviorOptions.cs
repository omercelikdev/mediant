using System.Text.Json;

namespace Mediant.Behaviors.Configuration;

/// <summary>
/// Options for the logging behavior.
/// </summary>
public sealed class LoggingBehaviorOptions
{
    /// <summary>
    /// Gets or sets property names to auto-mask in logs.
    /// </summary>
    public HashSet<string> MaskProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Secret", "Token", "ApiKey", "AccessToken", "RefreshToken",
        "CreditCard", "CardNumber", "Cvv", "Ssn", "SocialSecurityNumber"
    };

    /// <summary>
    /// Gets or sets the maximum serialized length before truncation.
    /// </summary>
    public int MaxSerializedLength { get; set; } = 4096;

    /// <summary>
    /// Gets or sets whether logging is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the performance behavior.
/// </summary>
public sealed class PerformanceBehaviorOptions
{
    /// <summary>
    /// Gets or sets the warning threshold in milliseconds. Default is 500ms.
    /// </summary>
    public int WarningThresholdMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets the critical threshold in milliseconds. Default is 5000ms.
    /// </summary>
    public int CriticalThresholdMs { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the hard ceiling in milliseconds above which a request is always logged as
    /// Critical, regardless of the warning/critical thresholds. Default is 30000 (30 seconds).
    /// Set to 0 or a negative value to disable the ceiling (e.g. when long-running batch commands
    /// are expected). Per-request override: <see cref="Attributes.PerformanceThresholdAttribute.CeilingMs"/>.
    /// </summary>
    public int HardCeilingMs { get; set; } = 30_000;

    /// <summary>
    /// Gets or sets whether performance monitoring is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the retry behavior.
/// </summary>
public sealed class RetryBehaviorOptions
{
    /// <summary>
    /// Gets or sets the default max retry count. Default is 3.
    /// </summary>
    public int DefaultMaxRetryCount { get; set; } = 3;

    /// <summary>
    /// Gets or sets the default initial delay in milliseconds. Default is 200.
    /// </summary>
    public int DefaultInitialDelayMs { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum backoff cap in seconds. Default is 30.
    /// </summary>
    public int MaxBackoffCapSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether retry is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the caching behavior.
/// </summary>
public sealed class CachingBehaviorOptions
{
    /// <summary>
    /// Gets or sets the default cache duration in seconds. Default is 300.
    /// </summary>
    public int DefaultDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum number of per-key locks retained for stampede prevention.
    /// Unused locks are evicted automatically. Default is 10,000.
    /// </summary>
    public int MaxLockPoolSize { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used to (de)serialize cached values.
    /// Set this to options backed by a <c>JsonSerializerContext</c> for trimming/Native AOT.
    /// When null, reflection-based defaults are used.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets whether caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the audit behavior.
/// </summary>
public sealed class AuditBehaviorOptions
{
    /// <summary>
    /// Gets or sets whether to audit commands. Default is true.
    /// </summary>
    public bool AuditCommands { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to audit queries. Default is false.
    /// </summary>
    public bool AuditQueries { get; set; }

    /// <summary>
    /// Gets or sets whether to fallback to console on store failure.
    /// </summary>
    public bool FallbackToConsole { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum response size in characters before truncation.
    /// </summary>
    public int MaxResponseSize { get; set; } = 4096;

    /// <summary>
    /// Gets or sets property name patterns to auto-mask.
    /// </summary>
    public HashSet<string> SensitivePatterns { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "Secret", "Token", "ApiKey", "CreditCard", "Ssn"
    };

    /// <summary>
    /// Gets or sets whether auditing is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the transaction behavior.
/// </summary>
public sealed class TransactionBehaviorOptions
{
    /// <summary>
    /// Gets or sets the default transaction timeout in seconds. Default is 30.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets whether transactions are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the authorization behavior.
/// </summary>
public sealed class AuthorizationBehaviorOptions
{
    /// <summary>
    /// Gets or sets whether authorization is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Options for the idempotency behavior.
/// </summary>
public sealed class IdempotencyBehaviorOptions
{
    /// <summary>
    /// Gets or sets the default idempotency window in seconds. Default is 300.
    /// </summary>
    public int DefaultWindowSeconds { get; set; } = 300;

    /// <summary>
    /// Gets or sets the <see cref="JsonSerializerOptions"/> used to (de)serialize stored responses
    /// in the distributed-cache store. Set this to options backed by a <c>JsonSerializerContext</c>
    /// for trimming/Native AOT. When null, reflection-based defaults are used.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Gets or sets whether idempotency is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
