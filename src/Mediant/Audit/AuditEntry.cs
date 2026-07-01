namespace Mediant.Audit;

/// <summary>
/// Represents an audit log entry for a mediator request.
/// </summary>
public sealed class AuditEntry
{
    /// <summary>
    /// Gets or sets the unique identifier for this audit entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the correlation identifier for tracking related operations.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the request type name.
    /// </summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the serialized request data (with sensitive fields masked).
    /// </summary>
    public string? RequestData { get; set; }

    /// <summary>
    /// Gets or sets the serialized response data (truncated if too large).
    /// </summary>
    public string? ResponseData { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who made the request.
    /// </summary>
    public string UserId { get; set; } = "SYSTEM";

    /// <summary>
    /// Gets or sets the user name who made the request.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the request was made.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the duration of the request in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Gets or sets whether the request was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the error message if the request failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception type if an exception occurred.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the action name from IAuditableRequest (e.g., "Order.Create").
    /// </summary>
    public string? ActionName { get; set; }

    /// <summary>
    /// Gets or sets the entity type from IAuditableRequest (e.g., "Order").
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// Gets or sets the entity identifier from IAuditableRequest (e.g., "12345").
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Gets or sets additional metadata about the request.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resets this entry for reuse from an object pool.
    /// </summary>
    internal void Reset()
    {
        Id = Guid.NewGuid().ToString("N");
        CorrelationId = string.Empty;
        RequestType = string.Empty;
        RequestData = null;
        ResponseData = null;
        UserId = "SYSTEM";
        UserName = null;
        Timestamp = DateTimeOffset.UtcNow;
        DurationMs = 0;
        IsSuccess = false;
        ErrorMessage = null;
        ExceptionType = null;
        ActionName = null;
        EntityType = null;
        EntityId = null;
        Metadata.Clear();
    }
}
