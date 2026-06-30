using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.Audit;

/// <summary>
/// In-memory implementation of <see cref="IAuditStore"/> for development and testing.
/// Thread-safe with an atomically enforced bound: when the store is full, the oldest
/// entry is evicted (ring buffer). Not suitable for production use with high throughput.
/// </summary>
public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly object _gate = new();
    private readonly Queue<AuditEntry> _entries = new();
    private readonly int _maxEntries;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryAuditStore"/>.
    /// </summary>
    /// <param name="maxEntries">The maximum number of entries to store. Defaults to 10,000.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxEntries"/> is not positive.</exception>
    public InMemoryAuditStore(int maxEntries = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            AddLocked(entry);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                AddLocked(entries[i]);
            }
        }

        return ValueTask.CompletedTask;
    }

    // Caller must hold _gate. Enforces the bound atomically by evicting the oldest entry.
    private void AddLocked(AuditEntry entry)
    {
        while (_entries.Count >= _maxEntries)
        {
            _entries.Dequeue();
        }

        _entries.Enqueue(entry);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        AuditEntry[] snapshot;
        lock (_gate)
        {
            snapshot = _entries.ToArray();
        }

        var results = new List<AuditEntry>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            var entry = snapshot[i];
            if (MatchesQuery(entry, query))
            {
                results.Add(entry);
            }
        }

        // Sort by timestamp descending
        results.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));

        var paged = results
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return new ValueTask<IReadOnlyList<AuditEntry>>(paged);
    }

    /// <summary>
    /// Gets all audit entries in insertion order (oldest first). For testing purposes.
    /// </summary>
    public IReadOnlyList<AuditEntry> GetAll()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// Clears all audit entries. For testing purposes.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private static bool MatchesQuery(AuditEntry entry, AuditQuery query)
    {
        if (query.CorrelationId is not null && !string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.RequestType is not null && !string.Equals(entry.RequestType, query.RequestType, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.UserId is not null && !string.Equals(entry.UserId, query.UserId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.From.HasValue && entry.Timestamp < query.From.Value)
        {
            return false;
        }

        if (query.To.HasValue && entry.Timestamp > query.To.Value)
        {
            return false;
        }

        if (query.IsSuccess.HasValue && entry.IsSuccess != query.IsSuccess.Value)
        {
            return false;
        }

        return true;
    }
}
