namespace Mediant.Abstractions;

/// <summary>
/// Marker interface for requests that provide structured audit metadata.
/// Implement this on command/query records to supply ActionName, EntityType, EntityId,
/// and custom metadata for the audit log. Works alongside [Auditable] attribute —
/// attribute enables audit logging, this interface enriches it with domain context.
/// </summary>
/// <example>
/// <code>
/// [Auditable]
/// public record CreateOrderCommand(string Product) : ICommand&lt;Result&gt;, IAuditableRequest
/// {
///     public string ActionName => "Order.Create";
///     public string? EntityType => "Order";
///     public string? EntityId => null; // set after creation
///     public object? AuditMetadata => new { Product };
/// }
/// </code>
/// </example>
public interface IAuditableRequest
{
    /// <summary>
    /// Gets the action name for audit logging (e.g., "Order.Create", "User.Delete").
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Gets the type of entity being acted upon (e.g., "Order", "User"), or null if not applicable.
    /// </summary>
    string? EntityType { get; }

    /// <summary>
    /// Gets the identifier of the entity being acted upon, or null if not applicable.
    /// </summary>
    string? EntityId { get; }

    /// <summary>
    /// Gets optional metadata to include in the audit entry. Serialized to JSON.
    /// </summary>
    object? AuditMetadata { get; }
}
