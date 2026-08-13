namespace Nordstein.Core.Common.Async;

/// <summary>
/// Keyed async lock. Unlike <c>lock</c>/<see cref="Monitor"/>, safe to hold across <c>await</c> boundaries.
/// </summary>
internal sealed class AsyncLock : IAsyncLock
{
    private readonly Dictionary<object, (SemaphoreSlim Semaphore, int Count)> locks = new();
    private readonly Lock sync = new();

    /// <summary>Synchronously acquires the lock for <paramref name="key"/>. Dispose the returned handle to release it.</summary>
    public IDisposable Lock(object key)
    {
        var semaphore = Acquire(key);
        try
        {
            semaphore.Wait();
        }
        catch
        {
            // The permit was never taken, so unwind only the refcount (no semaphore.Release()).
            Unwind(key);
            throw;
        }
        return new Releaser(this, key, semaphore);
    }

    /// <summary>Asynchronously acquires the lock for <paramref name="key"/>. Dispose the returned handle to release it.</summary>
    public async Task<IDisposable> LockAsync(object key, CancellationToken cancellationToken = default)
    {
        var semaphore = Acquire(key);
        try
        {
            await semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            // Cancellation (or any wait failure) means the permit was never taken: unwind the
            // refcount we incremented in Acquire so the entry/semaphore don't leak. Do not call
            // semaphore.Release() — we never acquired the slot.
            Unwind(key);
            throw;
        }
        return new Releaser(this, key, semaphore);
    }

    private SemaphoreSlim Acquire(object key)
    {
        lock (sync)
        {
            if (locks.TryGetValue(key, out var entry))
                locks[key] = (entry.Semaphore, entry.Count + 1);
            else
                locks[key] = (new SemaphoreSlim(1, 1), 1);

            return locks[key].Semaphore;
        }
    }

    private void Release(object key, SemaphoreSlim semaphore)
    {
        // Release the slot first so a waiter can proceed, then drop our refcount.
        semaphore.Release();
        Unwind(key);
    }

    // Drops one refcount for the key; disposes and removes the semaphore once no callers remain.
    private void Unwind(object key)
    {
        lock (sync)
        {
            var (semaphore, count) = locks[key];
            if (count == 1)
            {
                locks.Remove(key);
                semaphore.Dispose();
            }
            else
            {
                locks[key] = (semaphore, count - 1);
            }
        }
    }

    private sealed class Releaser : IDisposable
    {
        private readonly AsyncLock parent;
        private readonly object key;
        private readonly SemaphoreSlim semaphore;
        private bool disposed;

        public Releaser(AsyncLock parent, object key, SemaphoreSlim semaphore)
        {
            this.parent = parent;
            this.key = key;
            this.semaphore = semaphore;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            parent.Release(key, semaphore);
        }
    }
}
