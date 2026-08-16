using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Notifications;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Anomaly.Internal;

/// <summary>
/// Background pipeline that inspects completed test-run groups for negative anomalies and raises
/// notifications. Structurally mirrors <c>OptimizerService</c>: a queue fed from the test runner on
/// group completion, drained by a single worker loop. Input assembly lives in
/// <see cref="IAnomalyInputFactory"/> so the kiosk demo seeder can run the same rule engine.
/// </summary>
internal sealed class AnomalyDetectionService : BackgroundService, IAnomalyDetectionService
{
    private readonly IAnomalyDetector detector;
    private readonly IAnomalyInputFactory inputFactory;
    private readonly ITestRunGroupRepository testRunGroups;
    private readonly INotificationService notifications;
    private readonly ILogger<AnomalyDetectionService> logger;

    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyDetectionService"/> class.
    /// </summary>
    public AnomalyDetectionService(
        IAnomalyDetector detector,
        IAnomalyInputFactory inputFactory,
        ITestRunGroupRepository testRunGroups,
        INotificationService notifications,
        ILogger<AnomalyDetectionService> logger)
    {
        this.detector = detector;
        this.inputFactory = inputFactory;
        this.testRunGroups = testRunGroups;
        this.notifications = notifications;
        this.logger = logger;
    }

    /// <summary>
    /// Enqueues asynchronously.
    /// </summary>
    public Task EnqueueAsync(ITestRunGroup testRunGroup, CancellationToken cancellationToken = default)
        => channel.Writer.WriteAsync(testRunGroup.Id, cancellationToken).AsTask();

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var groupId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await DetectAsync(groupId, cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // individual job cancelled — continue processing
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Anomaly detection failed for test run group {GroupId}", groupId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    private async Task DetectAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var group = await testRunGroups.FindAsync(groupId, cancellationToken);
        if (group is null)
        {
            logger.LogWarning("Test run group {GroupId} not found — skipping anomaly detection", groupId);
            return;
        }

        var input = await inputFactory.BuildAsync(group, cancellationToken);
        var anomalies = detector.Detect(input);
        if (anomalies.Count == 0)
            return;

        logger.LogInformation(
            "Anomaly detection flagged {Count} issue(s) for test run group {GroupId}",
            anomalies.Count, groupId);

        foreach (var anomaly in anomalies)
        {
            await notifications.NotifyAsync(
                new NotificationRequest(
                    NotificationKind.Anomaly,
                    anomaly.Severity,
                    anomaly.Title,
                    anomaly.Message,
                    input.ProjectId,
                    anomaly.TargetKind,
                    anomaly.TargetId),
                cancellationToken);
        }
    }
}
