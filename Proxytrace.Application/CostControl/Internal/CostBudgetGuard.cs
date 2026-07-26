using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Notifications;
using Proxytrace.Common.Serialization;
using Proxytrace.Common.Time;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Statistics;
using Proxytrace.Licensing;

namespace Proxytrace.Application.CostControl.Internal;

/// <summary>
/// Periodically recomputes month-to-date spend and compares it against every enabled
/// <see cref="ICostLimit"/>, persisting a <see cref="ICostLimitBreach"/> the first time each
/// threshold is crossed in a month and raising the matching notification.
/// </summary>
/// <remarks>
/// Cost is never persisted per call, so spend is always re-derived from token counts × current
/// endpoint prices — a price correction reprices history and budgets follow. The breach row is the
/// only persisted state, which makes the "fire once per threshold per month" promise survive a
/// restart, and its unique index makes a concurrent double-fire impossible.
/// </remarks>
internal sealed class CostBudgetGuard : BackgroundService
{
    private readonly ICostStatistics costStatistics;
    private readonly ICostLimitRepository costLimits;
    private readonly ICostLimitBreachRepository breaches;
    private readonly ICostLimitBreach.CreateNew createBreach;
    private readonly INotificationService notifications;
    private readonly ILicenseService licenseService;
    private readonly ISerializer serializer;
    private readonly IClock clock;
    private readonly CostControlOptions options;
    private readonly ILogger<Audit> auditLogger;
    private readonly ILogger<CostBudgetGuard> logger;

    public CostBudgetGuard(
        ICostStatistics costStatistics,
        ICostLimitRepository costLimits,
        ICostLimitBreachRepository breaches,
        ICostLimitBreach.CreateNew createBreach,
        INotificationService notifications,
        ILicenseService licenseService,
        ISerializer serializer,
        IClock clock,
        CostControlOptions options,
        ILogger<Audit> auditLogger,
        ILogger<CostBudgetGuard> logger)
    {
        this.costStatistics = costStatistics;
        this.costLimits = costLimits;
        this.breaches = breaches;
        this.createBreach = createBreach;
        this.notifications = notifications;
        this.licenseService = licenseService;
        this.serializer = serializer;
        this.clock = clock;
        this.options = options;
        this.auditLogger = auditLogger;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, options.GuardIntervalSeconds));

        await EvaluateAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await EvaluateAsync(cancellationToken);
        }
    }

    /// <summary>
    /// One evaluation pass. Internal rather than private so tests can drive a single tick without
    /// starting the hosted service loop.
    /// </summary>
    internal async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Use-time degrade: an unlicensed install keeps its budget configuration but nothing
            // fires and nothing blocks, so re-licensing restores enforcement without re-entry.
            if (!licenseService.IsFeatureEnabled(LicenseFeature.CostControls))
                return;

            IReadOnlyList<ICostLimit> limits = await costLimits.GetAllEnabledAsync(cancellationToken);
            if (limits.Count == 0)
                return;

            DateTimeOffset monthStart = CostMonth.StartOf(clock.UtcNow);

            IReadOnlyList<ProjectAgentCostStat> spend =
                await costStatistics.GetMonthToDateSpendAsync(monthStart, cancellationToken);

            Dictionary<Guid, decimal> spendByProject = spend
                .GroupBy(s => s.ProjectId)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CostEur));

            Dictionary<Guid, decimal> spendByAgent = spend
                .GroupBy(s => s.AgentId)
                .ToDictionary(g => g.Key, g => g.Sum(s => s.CostEur));

            IReadOnlyList<ICostLimitBreach> existing = await breaches.GetForMonthAsync(monthStart, cancellationToken);
            HashSet<(Guid LimitId, CostThreshold Threshold)> fired = existing
                .Select(b => (b.CostLimit.Id, b.Threshold))
                .ToHashSet();

            foreach (ICostLimit limit in limits)
            {
                decimal effectiveSpend = limit.Agent is { } agent
                    ? spendByAgent.GetValueOrDefault(agent.Id)
                    : spendByProject.GetValueOrDefault(limit.Project.Id);

                // Soft before hard so a single tick that vaults past both still tells the whole
                // story: the warning explains the escalation the critical alert then acts on.
                if (limit.SoftLimitEur is { } soft && effectiveSpend >= soft
                    && !fired.Contains((limit.Id, CostThreshold.Soft)))
                {
                    await RecordBreachAsync(limit, monthStart, CostThreshold.Soft, effectiveSpend, soft, cancellationToken);
                }

                if (limit.HardLimitEur is { } hard && effectiveSpend >= hard
                    && !fired.Contains((limit.Id, CostThreshold.Hard)))
                {
                    await RecordBreachAsync(limit, monthStart, CostThreshold.Hard, effectiveSpend, hard, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to evaluate cost budgets");
        }
    }

    private async Task RecordBreachAsync(
        ICostLimit limit,
        DateTimeOffset monthStart,
        CostThreshold threshold,
        decimal spendEur,
        decimal thresholdEur,
        CancellationToken cancellationToken)
    {
        try
        {
            await breaches.AddAsync(
                createBreach(limit, monthStart, threshold, spendEur),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The unique index is what makes the double-fire impossible; losing the race here means
            // another instance already recorded and announced this crossing, so stay silent.
            logger.LogDebug(ex, "Cost limit {LimitId} breach ({Threshold}) already recorded", limit.Id, threshold);
            return;
        }

        string scope = limit.Agent is { } agent
            ? $"Agent '{agent.Name}' in project '{limit.Project.Name}'"
            : $"Project '{limit.Project.Name}'";

        (string title, string message, NotificationSeverity severity) = threshold switch
        {
            CostThreshold.Hard => (
                "Monthly cost budget exceeded",
                $"{scope} has reached its hard monthly budget of {Format(thresholdEur)} "
                + $"(month-to-date spend {Format(spendEur)}). Further proxied calls are rejected "
                + "until the budget resets or is raised.",
                NotificationSeverity.Critical),
            _ => (
                "Monthly cost budget warning",
                $"{scope} has reached its soft monthly budget of {Format(thresholdEur)} "
                + $"(month-to-date spend {Format(spendEur)}).",
                NotificationSeverity.Warning),
        };

        // Deliberately no TargetKind/TargetId: notification de-duplication is target-scoped but
        // kind-insensitive, so an unacknowledged soft alert would swallow the later hard alert for
        // the same limit. The breach row already guarantees one alert per threshold per month.
        await notifications.NotifyAsync(
            new NotificationRequest(
                NotificationKind.CostBudget,
                severity,
                title,
                message,
                limit.Project.Id),
            cancellationToken);

        auditLogger.LogAudit(
            threshold == CostThreshold.Hard
                ? AuditAction.CostBudgetHardLimitReached
                : AuditAction.CostBudgetSoftLimitReached,
            targetType: nameof(ICostLimit),
            targetId: limit.Id,
            targetLabel: limit.Agent?.Name ?? limit.Project.Name,
            projectId: limit.Project.Id,
            details: serializer.Serialize(new
            {
                Threshold = threshold.ToString(),
                ThresholdEur = thresholdEur,
                SpendEur = spendEur,
                MonthStart = monthStart,
            }));
    }

    private static string Format(decimal amount)
        => $"€{amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}";
}
