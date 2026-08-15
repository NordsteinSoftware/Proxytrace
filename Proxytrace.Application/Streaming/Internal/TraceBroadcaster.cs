using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Proxytrace.Application.Streaming.Internal;

internal class TraceBroadcaster : ITraceBroadcaster, IDisposable
{
    // Bounds total live SSE subscriptions so an authenticated client cannot exhaust memory/sockets by
    // opening unbounded streams. New subscriptions past the cap get an immediately-completed reader,
    // so the SSE request closes cleanly instead of accumulating. Every client subscribes to this
    // global trace stream, so the cap matters here above all.
    private const int MaxSubscribers = 2000;

    private readonly ConcurrentDictionary<Guid, ChannelWriter<TraceCreatedEvent>> traceSubscribers = new();

    /// <summary>
    /// Subscribes.
    /// </summary>
    public ChannelReader<TraceCreatedEvent> Subscribe(CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            throw new ArgumentException(
                "Subscription requires a cancellable token to avoid leaking subscribers.",
                nameof(cancellationToken));
        }

        var channel = Channel.CreateBounded<TraceCreatedEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        if (traceSubscribers.Count >= MaxSubscribers)
        {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var id = Guid.NewGuid();
        traceSubscribers[id] = channel.Writer;
        cancellationToken.Register(() =>
        {
            traceSubscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        });
        return channel.Reader;
    }

    /// <summary>
    /// Publishes.
    /// </summary>
    public void Publish(TraceCreatedEvent evt)
    {
        foreach (var kvp in traceSubscribers)
        {
            if (!kvp.Value.TryWrite(evt))
                traceSubscribers.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Releases all resources used by the current instance.
    /// </summary>
    public void Dispose()
    {
        var subscribers = traceSubscribers.Values.ToList();
        traceSubscribers.Clear();
        foreach (var writer in subscribers)
        {
            writer.TryComplete();
        }
    }
}
