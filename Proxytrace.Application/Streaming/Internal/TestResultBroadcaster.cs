using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Proxytrace.Application.Streaming.Internal;

internal class TestResultBroadcaster : ITestResultBroadcaster, IDisposable
{
    // Bounds total live SSE subscriptions so an authenticated client cannot exhaust memory/sockets by
    // opening unbounded run/group streams. New subscriptions past the cap get an immediately-completed
    // reader, so the SSE request closes cleanly instead of accumulating.
    private const int MaxSubscribers = 2000;

    // Keyed by run/group id so a published event reaches only that run's (or group's) subscribers,
    // instead of scanning every live subscriber on the instance per event. The inner map is keyed by
    // a per-subscription id so cleanup is O(1).
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>>
        runSubscribers = new();

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>>
        groupSubscribers = new();

    /// <summary>
    /// Subscribes.
    /// </summary>
    public ChannelReader<TestRunEvent> Subscribe(Guid runId, CancellationToken cancellationToken)
        => Add(runSubscribers, runId, cancellationToken);

    /// <summary>
    /// Subscribes the to group.
    /// </summary>
    public ChannelReader<TestRunEvent> SubscribeToGroup(Guid groupId, CancellationToken cancellationToken)
        => Add(groupSubscribers, groupId, cancellationToken);

    private ChannelReader<TestRunEvent> Add(
        ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>> target,
        Guid key,
        CancellationToken cancellationToken)
    {
        var channel = CreateChannel();
        if (AtCapacity())
        {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var subscriptionId = Guid.NewGuid();
        var bucket = target.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>());
        bucket[subscriptionId] = channel.Writer;

        cancellationToken.Register(() =>
        {
            RemoveSubscription(target, key, subscriptionId);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    private static void RemoveSubscription(
        ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>> target,
        Guid key,
        Guid subscriptionId)
    {
        if (!target.TryGetValue(key, out var bucket))
            return;
        bucket.TryRemove(subscriptionId, out _);
        // Drop the now-empty bucket so the outer map doesn't grow unboundedly with the number of
        // distinct runs/groups streamed over the lifetime of the process.
        if (bucket.IsEmpty)
            target.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, ChannelWriter<TestRunEvent>>>(key, bucket));
    }

    private bool AtCapacity() => TotalSubscribers() >= MaxSubscribers;

    private int TotalSubscribers()
    {
        var total = 0;
        foreach (var bucket in runSubscribers.Values)
            total += bucket.Count;
        foreach (var bucket in groupSubscribers.Values)
            total += bucket.Count;
        return total;
    }

    /// <summary>
    /// Publishes.
    /// </summary>
    public void Publish(TestRunEvent evt)
    {
        ForwardToRunSubscribers(evt);
        ForwardToGroupSubscribers(evt);
    }

    /// <summary>
    /// Publishes the complete.
    /// </summary>
    public void PublishComplete(RunCompleteEvent evt)
    {
        if (runSubscribers.TryRemove(evt.RunId, out var bucket))
        {
            foreach (var writer in bucket.Values)
            {
                writer.TryWrite(evt);
                writer.TryComplete();
            }
        }

        // Forward run-complete to group channel but do NOT close it yet —
        // the group stays open until PublishGroupComplete is called.
        ForwardToGroupSubscribers(evt);
    }

    /// <summary>
    /// Publishes the group complete.
    /// </summary>
    public void PublishGroupComplete(GroupRunCompleteEvent evt)
    {
        if (!groupSubscribers.TryRemove(evt.GroupId, out var bucket))
            return;
        foreach (var writer in bucket.Values)
        {
            writer.TryWrite(evt);
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        foreach (var bucket in runSubscribers.Values)
            foreach (var writer in bucket.Values)
                writer.TryComplete();
        runSubscribers.Clear();

        foreach (var bucket in groupSubscribers.Values)
            foreach (var writer in bucket.Values)
                writer.TryComplete();
        groupSubscribers.Clear();
    }

    private void ForwardToRunSubscribers(TestRunEvent evt)
    {
        if (!runSubscribers.TryGetValue(evt.RunId, out var bucket))
            return;
        foreach (var kvp in bucket)
        {
            if (!kvp.Value.TryWrite(evt))
                RemoveSubscription(runSubscribers, evt.RunId, kvp.Key);
        }
    }

    private void ForwardToGroupSubscribers(TestRunEvent evt)
    {
        if (evt.GroupId == Guid.Empty)
            return;
        if (!groupSubscribers.TryGetValue(evt.GroupId, out var bucket))
            return;
        foreach (var kvp in bucket)
        {
            if (!kvp.Value.TryWrite(evt))
                RemoveSubscription(groupSubscribers, evt.GroupId, kvp.Key);
        }
    }

    private static Channel<TestRunEvent> CreateChannel()
        => Channel.CreateBounded<TestRunEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
}
