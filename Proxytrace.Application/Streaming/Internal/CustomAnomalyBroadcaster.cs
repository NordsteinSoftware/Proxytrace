using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Proxytrace.Application.Streaming.Internal;

internal class CustomAnomalyBroadcaster : ICustomAnomalyBroadcaster, IDisposable
{
    // Bounds total live SSE subscriptions so an authenticated client cannot exhaust memory/sockets
    // by opening unbounded streams. New subscriptions past the cap get an immediately-completed
    // reader, so the SSE request closes cleanly instead of accumulating. Mirrors TraceBroadcaster.
    private const int MaxSubscribers = 2000;

    private readonly ConcurrentDictionary<Guid, ChannelWriter<AnomalyFlaggedEvent>> subscribers = new();

    /// <summary>
    /// Subscribes.
    /// </summary>
    public ChannelReader<AnomalyFlaggedEvent> Subscribe(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            throw new ArgumentException(
                "Subscription requires a cancellable token to avoid leaking subscribers.",
                nameof(cancellationToken));
        }

        var channel = Channel.CreateBounded<AnomalyFlaggedEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        if (subscribers.Count >= MaxSubscribers)
        {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var id = Guid.NewGuid();
        subscribers[id] = channel.Writer;
        cancellationToken.Register(() =>
        {
            subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    /// <summary>
    /// Publishes.
    /// </summary>
    public void Publish(AnomalyFlaggedEvent evt)
    {
        foreach (var kvp in subscribers)
        {
            if (!kvp.Value.TryWrite(evt))
                subscribers.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        var writers = subscribers.Values.ToList();
        subscribers.Clear();
        foreach (var writer in writers)
        {
            writer.TryComplete();
        }
    }
}
