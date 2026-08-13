using AwesomeAssertions;
using Nordstein.Core.Common.Async;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class AsyncLockTests
{
    [TestMethod]
    public void Lock_WithKey_ReturnsDisposable()
    {
        var asyncLock = new AsyncLock();

        using var handle = asyncLock.Lock("key");

        handle.Should().NotBeNull();
    }

    [TestMethod]
    public void Lock_WhenDisposed_AllowsReacquisition()
    {
        var asyncLock = new AsyncLock();

        asyncLock.Lock("key").Dispose();

        // If the lock was not released this would deadlock; timeout protects the test runner
        var acquired = Task.Run(() => asyncLock.Lock("key")).Wait(TimeSpan.FromSeconds(1));
        acquired.Should().BeTrue();
    }

    [TestMethod]
    public void Lock_DoubleDispose_DoesNotThrow()
    {
        var asyncLock = new AsyncLock();
        var handle = asyncLock.Lock("key");

        handle.Dispose();
        var act = handle.Dispose;

        act.Should().NotThrow();
    }

    [TestMethod]
    public void Lock_SameKey_MutuallyExcludes()
    {
        var asyncLock = new AsyncLock();
        var log = new List<string>();

        using (asyncLock.Lock("key"))
        {
            // Attempt to acquire on a background thread while we hold the lock
            var contender = Task.Run(() =>
            {
                using var inner = asyncLock.Lock("key");
                lock (log) log.Add("contender-acquired");
            });

            // Contender must not have acquired the lock yet
            contender.Wait(50).Should().BeFalse("lock should still be held");
            lock (log) log.Add("owner-releasing");
        } // releases here

        // Now contender should finish acquiring the released lock
        SpinWait.SpinUntil(() => { lock (log) return log.Contains("contender-acquired"); },
            TimeSpan.FromSeconds(2)).Should().BeTrue();

        log[0].Should().Be("owner-releasing");
        log[1].Should().Be("contender-acquired");
    }

    [TestMethod]
    public void Lock_DifferentKeys_DoNotBlock()
    {
        var asyncLock = new AsyncLock();

        using var _ = asyncLock.Lock("key-a");

        // Different key must be acquirable immediately
        var acquired = Task.Run(() => asyncLock.Lock("key-b")).Wait(TimeSpan.FromSeconds(1));
        acquired.Should().BeTrue();
    }

    [TestMethod]
    public async Task LockAsync_WithKey_ReturnsDisposable()
    {
        var asyncLock = new AsyncLock();

        using var handle = await asyncLock.LockAsync("key");

        handle.Should().NotBeNull();
    }

    [TestMethod]
    public async Task LockAsync_WhenDisposed_AllowsReacquisition()
    {
        var asyncLock = new AsyncLock();

        (await asyncLock.LockAsync("key")).Dispose();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var handle = await asyncLock.LockAsync("key", cts.Token);
        handle.Should().NotBeNull();
    }

    [TestMethod]
    public async Task LockAsync_SameKey_MutuallyExcludes()
    {
        var asyncLock = new AsyncLock();
        var log = new List<string>();

        using (await asyncLock.LockAsync("key"))
        {
            var contender = Task.Run(async () =>
            {
                using var inner = await asyncLock.LockAsync("key");
                lock (log) log.Add("contender-acquired");
            });

            await Task.Delay(50);
            (await Task.WhenAny(contender, Task.Delay(50)) == contender).Should().BeFalse(
                "lock should still be held");
            lock (log) log.Add("owner-releasing");
        }

        await Task.Delay(100);
        lock (log)
        {
            log[0].Should().Be("owner-releasing");
            log[1].Should().Be("contender-acquired");
        }
    }

    [TestMethod]
    public async Task LockAsync_DifferentKeys_DoNotBlock()
    {
        var asyncLock = new AsyncLock();

        using var _ = await asyncLock.LockAsync("key-a");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var handle = await asyncLock.LockAsync("key-b", cts.Token);
        handle.Should().NotBeNull();
    }

    [TestMethod]
    public async Task LockAsync_WhenWaitCancelled_DoesNotLeakAndKeyStaysUsable()
    {
        var asyncLock = new AsyncLock();

        // Hold the key so the next acquire must wait, then cancel that wait.
        using (await asyncLock.LockAsync("key"))
        {
            using var cts = new CancellationTokenSource();
            var pending = asyncLock.LockAsync("key", cts.Token);
            await cts.CancelAsync();

            var act = async () => await pending;
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        // The cancelled acquire must have unwound its refcount: a fresh acquire on the same key
        // succeeds immediately rather than deadlocking on a leaked permit.
        using var reacquire = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var handle = await asyncLock.LockAsync("key", reacquire.Token);
        handle.Should().NotBeNull();
    }

    [TestMethod]
    public async Task LockAsync_ConcurrentAccessOnSameKey_SerializesAccess()
    {
        var asyncLock = new AsyncLock();
        var counter = 0;
        var violations = 0;

        async Task IncrementSafely()
        {
            using var _ = await asyncLock.LockAsync("counter");
            var snapshot = counter;
            await Task.Yield(); // force a context switch mid-critical-section
            if (counter != snapshot) Interlocked.Increment(ref violations);
            counter++;
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => IncrementSafely()));

        counter.Should().Be(20);
        violations.Should().Be(0);
    }
}
