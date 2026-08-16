using JetBrains.Annotations;
using Proxytrace.Application.Anomaly;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Demo.Scenarios;

/// <summary>
/// Runs the real anomaly rule engine over the seed data's two incident groups — the freshly
/// regressed triage group (pass-rate collapse + latency spike vs. its three-run baseline) and the
/// endpoint-down tone group — and persists what it detects as notifications. Using the live
/// detector instead of hand-written alert text keeps the demo inbox honest: the wording, severity
/// and deep-link targets are exactly what a real deployment would produce, and the seed fails loud
/// if the data stops tripping the rules.
/// </summary>
[UsedImplicitly]
internal sealed class AnomalySeedScenario : IDemoScenario
{
    private readonly DemoSeedContext ctx;
    private readonly IAnomalyInputFactory inputFactory;
    private readonly IAnomalyDetector detector;
    private readonly INotification.CreateNew createNotification;
    private readonly INotificationRepository notifications;
    private readonly IRepository<ITestRunGroup> groupRepo;
    private readonly IReadOnlyList<IStatsProjector> runStatsProjectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalySeedScenario"/> class.
    /// </summary>
    public AnomalySeedScenario(
        DemoSeedContext ctx,
        IAnomalyInputFactory inputFactory,
        IAnomalyDetector detector,
        INotification.CreateNew createNotification,
        INotificationRepository notifications,
        IRepository<ITestRunGroup> groupRepo,
        IEnumerable<IStatsProjector> statsProjectors)
    {
        this.ctx = ctx;
        this.inputFactory = inputFactory;
        this.detector = detector;
        this.createNotification = createNotification;
        this.notifications = notifications;
        this.groupRepo = groupRepo;
        runStatsProjectors = statsProjectors
            .Where(p => p.EntityType == typeof(ITestRun))
            .ToList();
    }

    // After the statistics backfill (40) has backdated the run history (baselines need a
    // time-ordered window) and after the notification scenario (50), so the detector's alerts land
    // newest in the inbox.
    /// <summary>
    /// Gets the order.
    /// </summary>
    public int Order => 60;

    /// <summary>
    /// Seeds asynchronously.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Live stats projection runs debounced in a background worker, so at this point in the
        // seed the TestRunStats the baseline needs may not exist yet. Project the seeded runs
        // synchronously first (the upsert is idempotent, so the worker doing it again is fine).
        foreach (var run in ctx.AllRuns)
        {
            foreach (var projector in runStatsProjectors)
                await projector.ProjectAsync(run.Id, cancellationToken);
        }

        await DetectAndPersistAsync(ctx.RequireRegressedTriageGroup(), cancellationToken);
        await DetectAndPersistAsync(ctx.RequireFailedToneGroup(), cancellationToken);
    }

    private async Task DetectAndPersistAsync(ITestRunGroup group, CancellationToken cancellationToken)
    {
        // Reload: the backfill scenario rewrites groups to backdate them, so the ctx reference
        // may be stale.
        var fresh = await groupRepo.GetAsync(group.Id, cancellationToken);

        var input = await inputFactory.BuildAsync(fresh, cancellationToken);
        var anomalies = detector.Detect(input);
        if (anomalies.Count == 0)
        {
            // The seed data is deterministic; silence here means the data or the detector's
            // thresholds drifted and the showcase silently lost its anomaly story.
            throw new InvalidOperationException(
                $"Seeded group '{fresh.Suite.Name}' ({fresh.Id}) no longer trips the anomaly detector.");
        }

        foreach (var anomaly in anomalies)
        {
            await notifications.AddAsync(
                createNotification(
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
