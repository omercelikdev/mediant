using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Audit;

namespace Mediant.Behaviors.Audit;

/// <summary>
/// Buffering <see cref="IAuditStore"/> decorator: saves go to the process-wide
/// <see cref="AuditBuffer"/> and are flushed to <typeparamref name="TInner"/> in batches by the
/// background flusher (or explicitly via <see cref="FlushAsync"/>). Registered by
/// <c>AddMediantAuditBuffering&lt;TInner&gt;()</c>.
/// <para>
/// <b>Durability trade-off:</b> buffered entries not yet flushed are lost if the process crashes
/// (graceful shutdown flushes). Only opt in when your audit requirements tolerate that window.
/// </para>
/// </summary>
/// <typeparam name="TInner">The durable store that receives the batched writes.</typeparam>
public sealed class BufferedAuditStore<TInner> : IAuditStore
    where TInner : IAuditStore
{
    private readonly AuditBuffer _buffer;
    private readonly TInner _inner;
    private readonly AuditBufferOptions _options;

    /// <summary>Initializes a new instance of <see cref="BufferedAuditStore{TInner}"/>.</summary>
    public BufferedAuditStore(AuditBuffer buffer, TInner inner, IOptions<AuditBufferOptions> options)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken)
        => _buffer.EnqueueAsync(entry, cancellationToken);

    /// <inheritdoc />
    public async ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        for (int i = 0; i < entries.Count; i++)
        {
            await _buffer.EnqueueAsync(entries[i], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Queries the underlying store. Pending buffered entries are flushed first so a query
    /// observes everything saved before it (read-your-writes for tests and admin endpoints).
    /// </summary>
    public async ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken).ConfigureAwait(false);
        return await _inner.QueryAsync(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronously drains the buffer into the underlying store — the escape hatch for tests and
    /// shutdown paths that must not wait for the next background flush.
    /// </summary>
    public ValueTask<int> FlushAsync(CancellationToken cancellationToken = default)
        => _buffer.DrainAsync(_inner, _options.BatchSize, cancellationToken);
}
