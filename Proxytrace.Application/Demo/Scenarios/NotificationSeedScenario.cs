using Proxytrace.Domain;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.OptimizationProposal;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Seeds the static dashboard notifications (an already-read historical alert + a ready proposal)
/// so the top-bar notifications inbox has content to demo and screenshot in kiosk mode. Runs after
/// the proposal scenario so a real proposal exists to deep-link to. The live anomaly alerts are
/// produced by <see cref="AnomalySeedScenario"/> via the real detector, not written here.
/// </summary>
internal sealed class NotificationSeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly INotification.CreateNew createNotification;
    private readonly INotificationRepository notifications;
    private readonly IRepository<IOptimizationProposal> proposals;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationSeedScenario"/> class.
    /// </summary>
    public NotificationSeedScenario(
        DemoSeedContext ctx,
        INotification.CreateNew createNotification,
        INotificationRepository notifications,
        IRepository<IOptimizationProposal> proposals)
    {
        this.ctx = ctx;
        this.createNotification = createNotification;
        this.notifications = notifications;
        this.proposals = proposals;
    }

    // After OptimizationProposalSeedScenario (40) so a proposal is available to point at.
    /// <summary>
    /// Gets the order.
    /// </summary>
    public int Order => 50;

    private sealed record NotificationSpec(
        NotificationKind Kind,
        NotificationSeverity Severity,
        string Title,
        string Message,
        NotificationTargetKind? TargetKind,
        Guid? TargetId,
        bool Read);

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var projectId = ctx.RequireProject().Id;

        var proposal = (await proposals.GetAllAsync(cancellationToken)).FirstOrDefault();

        // Listed oldest-first; the repository returns notifications newest-first. The live anomaly
        // alerts are NOT faked here — AnomalySeedScenario (Order 60) runs the real detector over
        // the seeded incident groups and persists its output on top of these.
        var specs = new List<NotificationSpec>
        {
            new(
                NotificationKind.Anomaly,
                NotificationSeverity.Warning,
                "Latency increase on the Code Review agent",
                "Average response latency rose to 2.4s — about 38 % above this suite's recent baseline.",
                ctx.CodeReviewAgent is { } codeReview ? NotificationTargetKind.Agent : null,
                ctx.CodeReviewAgent?.Id,
                Read: true),

            new(
                NotificationKind.ProposalReady,
                NotificationSeverity.Info,
                "Optimization proposal ready for review",
                "Switching the Customer Support agent to Claude is projected to raise pass rate by 17 points.",
                proposal is null ? null : NotificationTargetKind.OptimizationProposal,
                proposal?.Id,
                Read: false),
        };

        foreach (var spec in specs)
        {
            var notification = createNotification(
                spec.Kind,
                spec.Severity,
                spec.Title,
                spec.Message,
                projectId,
                spec.TargetKind,
                spec.TargetId);

            notification = await notifications.AddAsync(notification, cancellationToken);

            if (spec.Read)
            {
                await notification.MarkRead(cancellationToken);
            }
        }
    }
}
