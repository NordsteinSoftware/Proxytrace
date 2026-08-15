using System.Threading.Channels;

namespace Proxytrace.Application.ErrorLog.Internal;

internal sealed class ErrorLogChannel : IErrorLogChannel
{
    private const int Capacity = 500;

    private readonly Channel<ErrorLogEntry> channel = Channel.CreateBounded<ErrorLogEntry>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Tries to the write.
    /// </summary>
    public bool TryWrite(ErrorLogEntry entry) => channel.Writer.TryWrite(entry);

    /// <summary>
    /// Reads the all asynchronously.
    /// </summary>
    public IAsyncEnumerable<ErrorLogEntry> ReadAllAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);
}
