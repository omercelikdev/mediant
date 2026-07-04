using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Audit;

namespace Mediant.Behaviors.Audit;

/// <summary>
/// Process-wide bounded buffer for audit entries. Writers enqueue; the background flusher (and the
/// synchronous flush escape hatch) drain into the underlying durable store in batches. When the
/// buffer is full, writers wait (backpressure) — audit entries are never dropped by the buffer.
/// </summary>
public sealed class AuditBuffer
{
    private readonly Channel<AuditEntry> _channel;

    /// <summary>Initializes a new instance of <see cref="AuditBuffer"/>.</summary>
    public AuditBuffer(IOptions<AuditBufferOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _channel = Channel.CreateBounded<AuditEntry>(new BoundedChannelOptions(options.Value.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    /// <summary>Number of entries currently buffered.</summary>
    public int Count => _channel.Reader.Count;

    /// <summary>Enqueues an entry, waiting when the buffer is full.</summary>
    public ValueTask EnqueueAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _channel.Writer.WriteAsync(entry, cancellationToken);
    }

    /// <summary>
    /// Drains everything currently buffered into <paramref name="target"/> in batches of
    /// <paramref name="batchSize"/> and returns the number of entries written. Does not wait for
    /// new entries; safe to call concurrently with writers and the background flusher.
    /// </summary>
    public async ValueTask<int> DrainAsync(IAuditStore target, int batchSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var written = 0;
        var batch = new List<AuditEntry>(Math.Min(batchSize, 1024));

        while (_channel.Reader.TryRead(out var entry))
        {
            batch.Add(entry);
            if (batch.Count >= batchSize)
            {
                await WriteBatchAsync(target, batch, cancellationToken).ConfigureAwait(false);
                written += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await WriteBatchAsync(target, batch, cancellationToken).ConfigureAwait(false);
            written += batch.Count;
        }

        return written;
    }

    // If the durable write fails, the already-dequeued entries are put back into the buffer before
    // rethrowing, so a transient store outage delays audit persistence instead of losing entries
    // (relative order across a re-enqueued batch is not preserved; entries carry timestamps).
    private async ValueTask WriteBatchAsync(IAuditStore target, List<AuditEntry> batch, CancellationToken cancellationToken)
    {
        try
        {
            await target.SaveBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            foreach (var entry in batch)
            {
                _channel.Writer.TryWrite(entry);
            }

            throw;
        }
    }
}
