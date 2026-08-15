using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Licensing;

namespace Proxytrace.Application.TestRun.Internal;

/// <summary>
/// Periodically polls for due <see cref="ITestRunSchedule"/>s and fires a background test run for
/// each, skipping schedules whose prior run is still in flight or when the feature is unlicensed.
/// </summary>
internal sealed class TestRunSchedulerService : BackgroundService
{
    private readonly ITestRunScheduleRepository schedules;
    private readonly ITestRunGroupRepository groups;
    private readonly ITestRunnerService runner;
    private readonly ILicenseService license;
    private readonly TestRunSchedulerConfiguration configuration;
    private readonly ILogger<TestRunSchedulerService> logger;
    private readonly IAgentRepository agents;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunSchedulerService"/> class.
    /// </summary>
    public TestRunSchedulerService(
        ITestRunScheduleRepository schedules,
        ITestRunGroupRepository groups,
        ITestRunnerService runner,
        ILicenseService license,
        TestRunSchedulerConfiguration configuration,
        ILogger<TestRunSchedulerService> logger,
        IAgentRepository agents,
        ILogger<Audit> audit)
    {
        this.schedules = schedules;
        this.groups = groups;
        this.runner = runner;
        this.license = license;
        this.configuration = configuration;
        this.logger = logger;
        this.agents = agents;
        this.audit = audit;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(configuration.TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await RunDueSchedulesAsync(DateTimeOffset.UtcNow, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Periodic test-run scheduler tick failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Service is shutting down; exit cleanly.
        }
    }

    internal async Task RunDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!license.IsFeatureEnabled(LicenseFeature.ScheduledTestRuns))
            return;

        var due = await schedules.GetDueAsync(now, cancellationToken);

        foreach (var schedule in due)
        {
            try
            {
                var recent = await groups.GetByScheduleAsync(schedule.Id, take: 1, cancellationToken);
                if (recent.Any(g => g.Status is TestRunStatus.Pending or TestRunStatus.Running))
                {
                    logger.LogInformation(
                        "Skipping schedule {ScheduleId} ({Name}): prior run still in flight",
                        schedule.Id, schedule.Name);
                    continue;
                }

                var group = await runner.RunInBackgroundAsync(
                    schedule.Suite, schedule.Endpoints.ToArray(), schedule.Id, cancellationToken: cancellationToken);
                await schedule.RecordFired(now, cancellationToken);

                // Scheduled fire: no request context, so this is attributed to the System actor.
                var projectId = await agents.GetProjectIdAsync(schedule.Suite.Agent.Id, cancellationToken);
                audit.LogAudit(AuditAction.TestRunStarted, nameof(ITestRunGroup), group.Id, schedule.Suite.Name, projectId: projectId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fire schedule {ScheduleId} ({Name})", schedule.Id, schedule.Name);
            }
        }
    }
}
