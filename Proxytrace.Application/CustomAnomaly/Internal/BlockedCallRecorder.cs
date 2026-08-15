using Microsoft.Extensions.Logging;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Application.CustomAnomaly.Internal;

/// <summary>
/// Records the aftermath of a proxy-blocked call once its trace is persisted: the attribution
/// result (which detector, which trigger), the SSE anomaly event, and the notification. The
/// blocked call is <em>not</em> enqueued for the LLM review — there is no provider response to
/// judge, and the call is already flagged (<c>OutlierFlags.Blocked</c>) and attributed.
/// </summary>
internal interface IBlockedCallRecorder
{
    Task RecordAsync(
        IAgentCall call,
        Guid detectorId,
        string detectorName,
        string matchedTriggerPattern,
        CancellationToken cancellationToken);
}

internal sealed class BlockedCallRecorder : IBlockedCallRecorder
{
    private readonly ICustomAnomalyResultRepository results;
    private readonly ICustomAnomalyResult.CreateNew createResult;
    private readonly ICustomAnomalyBroadcaster broadcaster;
    private readonly INotificationService notifications;
    private readonly ILogger<BlockedCallRecorder> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlockedCallRecorder"/> class.
    /// </summary>
    public BlockedCallRecorder(
        ICustomAnomalyResultRepository results,
        ICustomAnomalyResult.CreateNew createResult,
        ICustomAnomalyBroadcaster broadcaster,
        INotificationService notifications,
        ILogger<BlockedCallRecorder> logger)
    {
        this.results = results;
        this.createResult = createResult;
        this.broadcaster = broadcaster;
        this.notifications = notifications;
        this.logger = logger;
    }

    /// <summary>
    /// Record asynchronously.
    /// </summary>
    public async Task RecordAsync(
        IAgentCall call,
        Guid detectorId,
        string detectorName,
        string matchedTriggerPattern,
        CancellationToken cancellationToken)
    {
        // The detector may have been deleted between the proxy's (cached) match and this ingestion;
        // the attribution row's FK would then fail. The flag on the call itself survives either way,
        // so a lost attribution only degrades the drawer detail — log and carry on.
        try
        {
            var result = createResult(
                detectorId: detectorId,
                agentCallId: call.Id,
                projectId: call.Agent.Project.Id,
                matchedTrigger: matchedTriggerPattern,
                reasoning: "Blocked at the proxy before reaching the provider.");
            await results.AddAsync(result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to persist blocked-call attribution for call {CallId} (detector {DetectorId})",
                call.Id, detectorId);
        }

        broadcaster.Publish(new AnomalyFlaggedEvent(
            call.Id, call.Agent.Id, call.Agent.Project.Id, detectorId, detectorName, Blocked: true));

        await notifications.NotifyAsync(
            new NotificationRequest(
                NotificationKind.Anomaly,
                NotificationSeverity.Warning,
                $"Blocked request: {detectorName}",
                $"A request of agent '{call.Agent.Name}' matched trigger '{matchedTriggerPattern}' and was blocked before reaching the provider.",
                call.Agent.Project.Id,
                NotificationTargetKind.AgentCall,
                call.Id),
            cancellationToken);
    }
}
