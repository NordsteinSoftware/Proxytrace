using System.Threading.Channels;

namespace Proxytrace.Application.Streaming;

/// <summary>
/// Event raised when a anomaly flagged occurs.
/// </summary>
public record AnomalyFlaggedEvent(
    Guid AgentCallId,
    Guid AgentId,
    Guid ProjectId,
    Guid DetectorId,
    string DetectorName,
    bool Blocked = false);

/// <summary>
/// Broadcasts custom anomaly events.
/// </summary>
public interface ICustomAnomalyBroadcaster
{
    ChannelReader<AnomalyFlaggedEvent> Subscribe(CancellationToken cancellationToken);

    void Publish(AnomalyFlaggedEvent evt);
}
