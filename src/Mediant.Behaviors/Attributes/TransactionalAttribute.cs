namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a command for transactional execution. Queries are automatically skipped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TransactionalAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the transaction timeout in seconds. Default is 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}
