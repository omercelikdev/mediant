namespace Mediant.Audit;

/// <summary>
/// Represents query parameters for retrieving audit entries.
/// </summary>
public sealed class AuditQuery
{
    /// <summary>
    /// Gets or sets the correlation identifier to filter by.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the request type to filter by.
    /// </summary>
    public string? RequestType { get; set; }

    /// <summary>
    /// Gets or sets the user identifier to filter by.
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the start date to filter by.
    /// </summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>
    /// Gets or sets the end date to filter by.
    /// </summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>
    /// Gets or sets whether to filter by success/failure.
    /// </summary>
    public bool? IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of entries to return.
    /// </summary>
    public int Take { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of entries to skip.
    /// </summary>
    public int Skip { get; set; }
}
