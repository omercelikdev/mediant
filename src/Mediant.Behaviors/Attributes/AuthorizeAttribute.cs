namespace Mediant.Behaviors.Attributes;

/// <summary>
/// Marks a request as requiring authorization. Multiple attributes mean ALL must pass.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the required roles (comma-separated). All specified roles must be present.
    /// </summary>
    public string? Roles { get; set; }

    /// <summary>
    /// Gets or sets the policy name to evaluate.
    /// </summary>
    public string? Policy { get; set; }
}
