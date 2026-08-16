using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Proxytrace.Messaging.Internal;

/// <summary>
/// In-memory single-process transport. Preserves the original in-process channel behaviour for
/// the test suite and local single-process runs. Acknowledgement is a no-op because nothing is
/// redelivered.
/// </summary>
internal sealed class InProcessIngestionStream : IIngestionStream
{
    private readonly Channel<IngestEnvelope> channel = Channel.CreateUnbounded<IngestEnvelope>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    // The chosen channel does not support Reader.Count, so track the buffered depth ourselves:
    // incremented on publish, decremented as each envelope is pulled by the consumer.
    private long depth;

    /// <summary>
    /// Publishes asynchronously.
    /// </summary>
    public async Task PublishAsync(IngestMessage message, CancellationToken cancellationToken = default)
    {
        await channel.Writer.WriteAsync(
            new IngestEnvelope(Guid.NewGuid().ToString("N"), message),
            cancellationToken);
        Interlocked.Increment(ref depth);
    }

    /// <summary>
    /// Consume asynchronously.
    /// </summary>
    public async IAsyncEnumerable<IngestEnvelope> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (IngestEnvelope envelope in channel.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref depth);
            yield return envelope;
        }
    }

    /// <summary>
    /// Ack asynchronously.
    /// </summary>
    public Task AckAsync(string messageId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    // The channel drops anything that is not pulled; an unacked envelope is never redelivered.
    /// <summary>
    /// Gets the redelivers unacknowledged.
    /// </summary>
    public bool RedeliversUnacknowledged => false;

    /// <summary>
    /// Gets the queue depth asynchronously.
    /// </summary>
    public Task<long> GetQueueDepthAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Math.Max(0L, Interlocked.Read(ref depth)));
}
