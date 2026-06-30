using System.Collections.Concurrent;

namespace Qorpe.Mediator.Behaviors.Behaviors;

/// <summary>
/// A bounded pool of keyed <see cref="SemaphoreSlim"/> instances with automatic eviction
/// of unused entries. Prevents unbounded memory growth when cache keys have high cardinality.
/// <para>
/// Each entry is reference-counted for the whole time a caller holds or awaits it, so an entry
/// can never be evicted while it is in use. Without that guarantee two callers could end up
/// serializing on <em>different</em> semaphores for the same key, silently defeating stampede
/// prevention and idempotency serialization.
/// </para>
/// </summary>
internal sealed class BoundedLockPool
{
    private readonly ConcurrentDictionary<string, LockEntry> _locks = new(StringComparer.Ordinal);
    private readonly int _maxSize;
    private readonly TimeSpan _evictionInterval;
    private long _lastEvictionTicks;

    internal int Count => _locks.Count;

    internal BoundedLockPool(int maxSize = 10_000, TimeSpan? evictionInterval = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize);
        _maxSize = maxSize;
        _evictionInterval = evictionInterval ?? TimeSpan.FromMinutes(5);
        _lastEvictionTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Acquires the per-key lock. Dispose the returned <see cref="Releaser"/> to release it.
    /// The underlying entry is reference-counted and cannot be evicted until released.
    /// </summary>
    internal async ValueTask<Releaser> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        var entry = Rent(key);
        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Return(entry);
            throw;
        }

        return new Releaser(this, entry);
    }

    private LockEntry Rent(string key)
    {
        while (true)
        {
            var entry = _locks.GetOrAdd(key, static _ => new LockEntry());
            Interlocked.Increment(ref entry.RefCount);

            // Re-validate: if the entry was evicted between GetOrAdd and the increment, discard
            // it and retry so every caller for this key converges on the live instance.
            if (_locks.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
            {
                entry.Touch();
                return entry;
            }

            Interlocked.Decrement(ref entry.RefCount);
        }
    }

    private void Return(LockEntry entry)
    {
        entry.Touch();
        Interlocked.Decrement(ref entry.RefCount);
        EvictIfNeeded();
    }

    private void EvictIfNeeded()
    {
        var now = Environment.TickCount64;
        var lastEviction = Interlocked.Read(ref _lastEvictionTicks);

        if (_locks.Count < _maxSize && now - lastEviction < _evictionInterval.TotalMilliseconds)
        {
            return;
        }

        // Only one thread should evict at a time.
        if (Interlocked.CompareExchange(ref _lastEvictionTicks, now, lastEviction) != lastEviction)
        {
            return;
        }

        EvictStale(now);
    }

    /// <summary>
    /// Forces eviction of stale, unreferenced entries. Primarily used for testing.
    /// </summary>
    internal void ForceEviction() => EvictStale(Environment.TickCount64);

    private void EvictStale(long now)
    {
        var threshold = now - (long)_evictionInterval.TotalMilliseconds;

        foreach (var kvp in _locks)
        {
            var entry = kvp.Value;
            // Never evict an entry that is referenced (held or awaited) by any caller.
            if (Volatile.Read(ref entry.RefCount) == 0 &&
                entry.LastAccessedTicks < threshold &&
                entry.Semaphore.CurrentCount > 0)
            {
                _locks.TryRemove(kvp.Key, out _);
            }
        }
    }

    internal readonly struct Releaser : IDisposable
    {
        private readonly BoundedLockPool _pool;
        private readonly LockEntry _entry;

        internal Releaser(BoundedLockPool pool, LockEntry entry)
        {
            _pool = pool;
            _entry = entry;
        }

        public void Dispose()
        {
            _entry.Semaphore.Release();
            _pool.Return(_entry);
        }
    }

    internal sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public long LastAccessedTicks;
        public int RefCount;

        public LockEntry()
        {
            LastAccessedTicks = Environment.TickCount64;
        }

        public void Touch()
        {
            Interlocked.Exchange(ref LastAccessedTicks, Environment.TickCount64);
        }
    }
}
