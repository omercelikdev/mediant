using Microsoft.EntityFrameworkCore;
using Mediant.Abstractions;

namespace Mediant.EntityFrameworkCore;

/// <summary>
/// Durable <see cref="IOutboxStore"/> backed by an EF Core <typeparamref name="TContext"/> with a
/// mapped <see cref="OutboxMessage"/> entity (see <c>ModelBuilder.ConfigureMediantOutbox()</c>).
/// <para>
/// <see cref="AddAsync"/> only tracks the message; it is persisted atomically with your business
/// data when you call <c>SaveChanges</c> on the same context — that is what makes the outbox
/// transactional.
/// </para>
/// </summary>
public sealed class EfOutboxStore<TContext> : IClaimingOutboxStore
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
            message.ClaimedBy = null;
            message.ClaimedUntil = null;
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
            // Release the claim so any instance can retry on its next poll instead of waiting for
            // the lease to expire.
            message.ClaimedBy = null;
            message.ClaimedUntil = null;
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxMessage>> ClaimPendingAsync(
        string ownerId, int batchSize, int maxAttempts, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now + leaseDuration;

        // Non-relational providers (EF InMemory in tests) cannot translate the set-based UPDATE;
        // fall back to a tracked update with single-instance semantics.
        if (!_context.Database.IsRelational())
        {
            return await ClaimTrackedAsync(ownerId, batchSize, maxAttempts, now, leaseUntil, cancellationToken).ConfigureAwait(false);
        }

        // Two-step claim that is safe across concurrent instances: select candidates, then a
        // guarded set-based UPDATE (single SQL statement). The guard re-checks the dispatchable
        // predicate, so of two racing instances only one wins each row; the loser's UPDATE simply
        // matches fewer rows. Finally load the rows this owner actually claimed.
        var candidateIds = await Dispatchable(now, maxAttempts)
            .OrderBy(m => m.OccurredOn)
            .Take(batchSize)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidateIds.Count == 0)
        {
            return [];
        }

        var claimed = await Dispatchable(now, maxAttempts)
            .Where(m => candidateIds.Contains(m.Id))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.ClaimedBy, ownerId)
                    .SetProperty(m => m.ClaimedUntil, leaseUntil),
                cancellationToken)
            .ConfigureAwait(false);

        if (claimed == 0)
        {
            return [];
        }

        return await _context.Set<OutboxMessage>()
            .Where(m => m.ClaimedBy == ownerId && m.ClaimedUntil == leaseUntil && m.ProcessedOn == null)
            .OrderBy(m => m.OccurredOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<OutboxMessage> Dispatchable(DateTimeOffset now, int maxAttempts)
        => _context.Set<OutboxMessage>().Where(m =>
            m.ProcessedOn == null &&
            m.Attempts < maxAttempts &&
            (m.ClaimedUntil == null || m.ClaimedUntil < now));

    private async ValueTask<IReadOnlyList<OutboxMessage>> ClaimTrackedAsync(
        string ownerId, int batchSize, int maxAttempts, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken)
    {
        var messages = await Dispatchable(now, maxAttempts)
            .OrderBy(m => m.OccurredOn)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            message.ClaimedBy = ownerId;
            message.ClaimedUntil = leaseUntil;
        }

        if (messages.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return messages;
    }
}
