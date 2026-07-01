using Mediant.Abstractions;

namespace Mediant.Behaviors.UserContext;

/// <summary>
/// User context provider that always returns "SYSTEM".
/// Used as fallback when no HTTP context is available.
/// </summary>
public sealed class SystemUserContextProvider : IAuditUserContext
{
    /// <inheritdoc />
    public string? GetUserId() => "SYSTEM";

    /// <inheritdoc />
    public string? GetUserName() => "System";
}
