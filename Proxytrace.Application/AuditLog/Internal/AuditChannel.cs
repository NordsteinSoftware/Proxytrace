using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace Proxytrace.Application.AuditLog.Internal;

internal sealed class AuditChannel : IAuditChannel
{
    // Unbounded + lossless: audit events are low-frequency (deliberate admin actions, not the
    // error/trace hot path), so TryWrite always succeeds and never drops, unlike the bounded
    // drop-oldest error-log channel. A stalled writer would grow memory; acceptable at this cardinality.
    private readonly Channel<AuditCapture> channel = Channel.CreateUnbounded<AuditCapture>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Tries to the write.
    /// </summary>
    public bool TryWrite(AuditCapture entry) => channel.Writer.TryWrite(entry);

    /// <summary>
    /// Reads the all asynchronously.
    /// </summary>
    public IAsyncEnumerable<AuditCapture> ReadAllAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>
    /// Tries to the read.
    /// </summary>
    public bool TryRead([MaybeNullWhen(false)] out AuditCapture entry)
        => channel.Reader.TryRead(out entry);
}
