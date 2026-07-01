using Mediant.Abstractions;

namespace Mediant.Audit;

/// <summary>
/// A no-op implementation of <see cref="IAuditStore"/> that discards all entries.
/// Used as the default when no audit store is configured.
/// </summary>
public sealed class NullAuditStore : IAuditStore
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly NullAuditStore Instance = new();

    /// <inheritdoc />
    public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
        => new(Array.Empty<AuditEntry>());
}
