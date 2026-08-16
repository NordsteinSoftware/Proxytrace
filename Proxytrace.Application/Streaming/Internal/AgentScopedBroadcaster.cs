using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Proxytrace.Application.Streaming.Internal;

/// <summary>
/// Shared fan-out plumbing for SSE broadcasters keyed by agent. Subscribers register per
/// agent id; <see cref="Publish"/> writes an event to every subscriber of its agent. Each
/// subscription gets a bounded channel that drops the oldest event when a slow reader falls
/// behind, and is cleaned up when its cancellation token fires.
/// </summary>
internal abstract class AgentScopedBroadcaster<TEvent> : IDisposable
{
    // Bounds total live SSE subscriptions across all agents so an authenticated client can't exhaust
    // memory/sockets by opening unbounded streams. Over the cap, Subscribe returns a completed reader.
    private const int MaxSubscribers = 2000;

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, ChannelWriter<TEvent>>> subscribers = new();

    /// <summary>Extracts the agent id an event should be delivered to.</summary>
    protected abstract Guid KeyOf(TEvent evt);

    /// <summary>
    /// Subscribes.
    /// </summary>
    public ChannelReader<TEvent> Subscribe(Guid agentId, CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        if (TotalSubscribers() >= MaxSubscribers)
        {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var subscriptionId = Guid.NewGuid();
        var agentSubscribers = subscribers.GetOrAdd(agentId, _ => new ConcurrentDictionary<Guid, ChannelWriter<TEvent>>());
        agentSubscribers[subscriptionId] = channel.Writer;

        cancellationToken.Register(() =>
        {
            if (subscribers.TryGetValue(agentId, out var subs))
            {
                subs.TryRemove(subscriptionId, out _);
                // Drop the now-empty per-agent bucket so the outer map doesn't grow unboundedly with
                // the number of distinct agents streamed over the lifetime of the process.
                if (subs.IsEmpty)
                    subscribers.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, ChannelWriter<TEvent>>>(agentId, subs));
            }
            channel.Writer.TryComplete();
        });

        return channel.Reader;
    }

    private int TotalSubscribers()
    {
        var total = 0;
        foreach (var agentSubscribers in subscribers.Values)
            total += agentSubscribers.Count;
        return total;
    }

    /// <summary>
    /// Publishes.
    /// </summary>
    public void Publish(TEvent evt)
    {
        if (!subscribers.TryGetValue(KeyOf(evt), out var agentSubscribers))
            return;

        foreach (var kvp in agentSubscribers)
        {
            if (!kvp.Value.TryWrite(evt))
                agentSubscribers.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        foreach (var (_, agentSubscribers) in subscribers)
        {
            foreach (var writer in agentSubscribers.Values)
                writer.TryComplete();
        }
        subscribers.Clear();
    }
}
