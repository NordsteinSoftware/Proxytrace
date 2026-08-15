using System.Threading.Channels;
using JetBrains.Annotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Notifications;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Notification;
using Proxytrace.Licensing;

namespace Proxytrace.Application.CustomAnomaly.Internal;

/// <summary>
/// Background pipeline that reviews freshly ingested agent calls against the project's custom
/// anomaly detectors. Structurally mirrors <see cref="Anomaly.Internal.AnomalyDetectionService"/>:
/// a queue fed from the ingestion processor, drained by a single worker loop. Per call it
/// scope-filters the enabled detectors, runs the cheap trigger match, and only on a hit invokes
/// the detector's hidden system agent (with <c>skipIngestion</c>, so a review can never re-enter
/// ingestion). An anomalous verdict persists an <see cref="ICustomAnomalyResult"/>, sets the
/// <see cref="OutlierFlags.CustomAnomaly"/> bit, broadcasts the SSE event, and raises a
/// notification.
/// </summary>
internal sealed class CustomAnomalyReviewService : BackgroundService, ICustomAnomalyReviewQueue
{
    /// <summary>Cap on the turn text handed to the judge, so a huge turn cannot blow the context.</summary>
    private const int MaxReviewTextLength = 16_000;

    private readonly ILicenseService license;
    private readonly IAgentCallRepository agentCalls;
    private readonly ICustomAnomalyDetectorRepository detectors;
    private readonly ICustomAnomalyResultRepository results;
    private readonly ICustomAnomalyResult.CreateNew createResult;
    private readonly ICustomAnomalyBroadcaster broadcaster;
    private readonly INotificationService notifications;
    private readonly ILogger<CustomAnomalyReviewService> logger;

    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyReviewService"/> class.
    /// </summary>
    public CustomAnomalyReviewService(
        ILicenseService license,
        IAgentCallRepository agentCalls,
        ICustomAnomalyDetectorRepository detectors,
        ICustomAnomalyResultRepository results,
        ICustomAnomalyResult.CreateNew createResult,
        ICustomAnomalyBroadcaster broadcaster,
        INotificationService notifications,
        ILogger<CustomAnomalyReviewService> logger)
    {
        this.license = license;
        this.agentCalls = agentCalls;
        this.detectors = detectors;
        this.results = results;
        this.createResult = createResult;
        this.broadcaster = broadcaster;
        this.notifications = notifications;
        this.logger = logger;
    }

    /// <summary>
    /// Enqueues asynchronously.
    /// </summary>
    public Task EnqueueAsync(Guid agentCallId, CancellationToken cancellationToken = default)
        => channel.Writer.WriteAsync(agentCallId, cancellationToken).AsTask();

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var callId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await ReviewAsync(callId, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // individual job cancelled — continue processing
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Custom anomaly review failed for agent call {CallId}", callId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    // Internal (not private) so tests can drive a single review deterministically without racing
    // the background loop.
    internal async Task ReviewAsync(Guid callId, CancellationToken cancellationToken)
    {
        // Dormant without the feature — queued ids are drained and dropped, so a downgrade simply
        // pauses reviews and a re-upgrade resumes them for new traffic.
        if (!license.IsFeatureEnabled(LicenseFeature.CustomAnomalyDetectors))
            return;

        var call = await agentCalls.FindAsync(callId, cancellationToken);
        if (call is null)
        {
            logger.LogWarning("Agent call {CallId} not found — skipping custom anomaly review", callId);
            return;
        }

        var text = ExtractTurnText(call);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var enabled = await detectors.GetEnabledByProjectAsync(call.Agent.Project.Id, cancellationToken);

        foreach (var detector in enabled)
        {
            if (!detector.AllAgents && detector.ScopedAgents.All(a => a.Id != call.Agent.Id))
                continue;

            var match = TriggerMatcher.FindFirstMatch(text, detector.Triggers);
            if (match is null)
                continue;

            // One review per (call, detector); a failing detector must not break the others.
            try
            {
                await RunReviewAsync(detector, call, text, match, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex, "Custom anomaly detector {DetectorId} failed reviewing call {CallId}",
                    detector.Id, call.Id);
            }
        }
    }

    private async Task RunReviewAsync(
        ICustomAnomalyDetector detector,
        IAgentCall call,
        string text,
        TriggerMatch match,
        CancellationToken cancellationToken)
    {
        var conversation = Conversation.Create().With(BuildReviewMessage(text, match));

        // skipIngestion is load-bearing: the judge's own call must never be recorded as a trace,
        // or every review would enqueue another review.
        using var client = detector.Agent.CreateClient(skipIngestion: true);
        var completion = await client.CompleteAsync<CustomAnomalyVerdict>(
            conversation,
            cancellationToken: cancellationToken);

        if (completion.Response is not { IsAnomalous: true } verdict)
            return;

        var result = createResult(
            detectorId: detector.Id,
            agentCallId: call.Id,
            projectId: call.Agent.Project.Id,
            matchedTrigger: match.Trigger.Pattern,
            reasoning: verdict.Reasoning);
        await results.AddAsync(result, cancellationToken);

        await agentCalls.SetOutlierFlagAsync(call.Id, OutlierFlags.CustomAnomaly, cancellationToken);

        broadcaster.Publish(new AnomalyFlaggedEvent(
            call.Id, call.Agent.Id, call.Agent.Project.Id, detector.Id, detector.Name));

        await notifications.NotifyAsync(
            new NotificationRequest(
                NotificationKind.Anomaly,
                NotificationSeverity.Warning,
                $"Anomaly detected: {detector.Name}",
                verdict.Reasoning ?? $"A conversation turn of agent '{call.Agent.Name}' matched trigger '{match.Trigger.Pattern}' and was judged anomalous.",
                call.Agent.Project.Id,
                NotificationTargetKind.AgentCall,
                call.Id),
            cancellationToken);
    }

    /// <summary>
    /// The NEW turn only: the request's last non-system message (the latest user/tool input — the
    /// earlier ones were reviewed with their own calls) plus the assistant's response, truncated to
    /// <see cref="MaxReviewTextLength"/>.
    /// </summary>
    private static string ExtractTurnText(IAgentCall call)
    {
        var lastRequestText = call.Request.Messages
            .LastOrDefault(m => m.Role != Role.System)
            ?.GetText();
        var responseText = call.Response?.Response.GetText();

        var text = string.Join(
            "\n\n",
            new[] { lastRequestText, responseText }.Where(t => !string.IsNullOrWhiteSpace(t)));

        return text.Length <= MaxReviewTextLength ? text : text[..MaxReviewTextLength];
    }

    private static UserMessage BuildReviewMessage(string text, TriggerMatch match)
    {
        // The detector's review instructions live in the judge agent's system prompt; this message
        // only carries the evidence (mirrors AgenticEvaluator.BuildEvaluationMessage).
        string content = $"""
                          # CONVERSATION TURN
                          "{text}"

                          # MATCHED TRIGGER
                          Pattern "{match.Trigger.Pattern}" matched the excerpt "{match.Excerpt}".
                          """;

        return Message.CreateUserMessage(content);
    }

    [UsedImplicitly]
    private sealed record CustomAnomalyVerdict(bool IsAnomalous, string? Reasoning);
}
