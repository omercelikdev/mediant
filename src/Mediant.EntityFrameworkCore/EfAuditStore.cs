using Microsoft.EntityFrameworkCore;
using Mediant.Abstractions;
using Mediant.Audit;

namespace Mediant.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IAuditStore"/> backed by an EF Core <typeparamref name="TContext"/> with a
/// mapped <see cref="AuditEntry"/> entity (see <c>ModelBuilder.ConfigureMediantAudit()</c>).
/// </summary>
public sealed class EfAuditStore<TContext> : IAuditStore
    where TContext : DbContext
{
    private readonly TContext _context;

    /// <summary>Initializes a new instance of <see cref="EfAuditStore{TContext}"/>.</summary>
    public EfAuditStore(TContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _context.Set<AuditEntry>().AddAsync(entry, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SaveBatchAsync(IReadOnlyList<AuditEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        await _context.Set<AuditEntry>().AddRangeAsync(entries, cancellationToken).ConfigureAwait(false);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<AuditEntry> q = _context.Set<AuditEntry>();

        if (query.CorrelationId is not null)
        {
            q = q.Where(e => e.CorrelationId == query.CorrelationId);
        }

        if (query.RequestType is not null)
        {
            q = q.Where(e => e.RequestType == query.RequestType);
        }

        if (query.UserId is not null)
        {
            q = q.Where(e => e.UserId == query.UserId);
        }

        if (query.From.HasValue)
        {
            q = q.Where(e => e.Timestamp >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            q = q.Where(e => e.Timestamp <= query.To.Value);
        }

        if (query.IsSuccess.HasValue)
        {
            q = q.Where(e => e.IsSuccess == query.IsSuccess.Value);
        }

        return await q
            .OrderByDescending(e => e.Timestamp)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
