namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a request for audit logging.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuditableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether to include the request body in the audit log. Default is true.
    /// </summary>
    public bool IncludeRequestBody { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include the response body in the audit log. Default is false.
    /// </summary>
    public bool IncludeResponseBody { get; set; }

    /// <summary>
    /// Gets or sets the maximum response size in characters before truncation. Default is 4096.
    /// </summary>
    public int MaxResponseSize { get; set; } = 4096;
}
