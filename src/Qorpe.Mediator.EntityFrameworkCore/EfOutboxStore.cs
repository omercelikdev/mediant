using Microsoft.EntityFrameworkCore;
using Qorpe.Mediator.Abstractions;

namespace Qorpe.Mediator.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IOutboxStore"/> backed by an EF Core <typeparamref name="TContext"/> with a
/// mapped <see cref="OutboxMessage"/> entity (see <c>ModelBuilder.ConfigureQorpeOutbox()</c>).
/// <para>
/// <see cref="AddAsync"/> only tracks the message; it is persisted atomically with your business
/// data when you call <c>SaveChanges</c> on the same context — that is what makes the outbox
/// transactional.
/// </para>
/// </summary>
public sealed class EfOutboxStore<TContext> : IOutboxStore
    where TContext : DbContext
{
    private readonly TContext _context;

    /// <summary>Initializes a new instance of <see cref="EfOutboxStore{TContext}"/>.</summary>
    public EfOutboxStore(TContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        await _context.Set<OutboxMessage>().AddAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken)
    {
        return await _context.Set<OutboxMessage>()
            .Where(m => m.ProcessedOn == null)
            .OrderBy(m => m.OccurredOn)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask MarkProcessedAsync(Guid id, DateTimeOffset processedOn, CancellationToken cancellationToken)
    {
        var message = await _context.Set<OutboxMessage>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (message is not null)
        {
            message.ProcessedOn = processedOn;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask MarkFailedAsync(Guid id, int attempts, string error, CancellationToken cancellationToken)
    {
        var message = await _context.Set<OutboxMessage>().FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (message is not null)
        {
            message.Attempts = attempts;
            message.Error = error;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
