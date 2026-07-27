using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Notifications;
using Proxytrace.Common.Time;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Notification;
using Proxytrace.Domain.Project;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// Periodically recomputes whether the current calendar month's trace count has exceeded the
/// licensed <see cref="LicenseLimit.MaxTracesPerMonth"/> cap, per project as well as overall.
/// Ingestion reads the cached decision to decide whether to drop an incoming trace, avoiding a
/// database count on every captured call.
/// </summary>
/// <remarks>
/// <para>
/// The cap is installation-wide, but the drop decision is not: enforcing it as one global switch let
/// a single busy project consume the whole month's allowance and silently stop capture for every
/// other project. Counts are therefore tracked per project and the decision applies a fair-share
/// rule — see <see cref="ITraceQuotaGuard.IsOverQuota"/>.
/// </para>
/// <para>
/// Dropping is also no longer silent. A dropped trace is acknowledged to the client (there is
/// nothing useful it could do with a rejection, and failing the proxied call would take the caller's
/// application down over a billing limit), so without something surfacing it, an operator's only
/// symptom was traces quietly going missing. Entering the dropping state raises a notification and
/// logs at Error, which is what the operator Error Log captures.
/// </para>
/// </remarks>
internal sealed class TraceQuotaGuard : BackgroundService, ITraceQuotaGuard
{
    private static readonly TimeSpan RecomputeInterval = TimeSpan.FromMinutes(5);

    // Near the cap the ordinary interval is far too coarse — a busy install can ingest a long way
    // past the limit inside five minutes. Once usage is close, recompute far more often.
    private static readonly TimeSpan NearLimitRecomputeInterval = TimeSpan.FromSeconds(30);
    private const double NearLimitFraction = 0.9;

    private readonly IAgentCallRepository agentCalls;
    private readonly IProjectRepository projects;
    private readonly ILicenseService licenseService;
    private readonly INotificationService notifications;
    private readonly IClock clock;
    private readonly ILogger<TraceQuotaGuard> logger;

    private volatile bool overQuota;

    // Per-project month counts and the fair share each is measured against. Replaced wholesale on
    // each recompute, so a reader never sees a half-updated map.
    private volatile IReadOnlyDictionary<Guid, long> projectCounts = new Dictionary<Guid, long>();
    private long fairShare = long.MaxValue;

    // Projects already reported as throttled this month, so the notification fires on entering the
    // state rather than on every recompute for as long as it lasts.
    private readonly ConcurrentDictionary<Guid, string> reportedProjects = new();
    private string reportedMonth = string.Empty;

    public TraceQuotaGuard(
        IAgentCallRepository agentCalls,
        IProjectRepository projects,
        ILicenseService licenseService,
        INotificationService notifications,
        IClock clock,
        ILogger<TraceQuotaGuard> logger)
    {
        this.agentCalls = agentCalls;
        this.projects = projects;
        this.licenseService = licenseService;
        this.notifications = notifications;
        this.clock = clock;
        this.logger = logger;
    }

    public bool IsCurrentMonthOverQuota => overQuota;

    public bool IsOverQuota(Guid projectId)
    {
        if (!overQuota)
        {
            return false;
        }

        long share = Interlocked.Read(ref fairShare);

        // A project with no counted traces yet is never the one to throttle. Unknown ids (a project
        // created since the last recompute) land here too, which is the right way round: a brand-new
        // project has consumed nothing.
        return projectCounts.TryGetValue(projectId, out long count) && count >= share;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await RecomputeAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(NextInterval(), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await RecomputeAsync(cancellationToken);
        }
    }

    /// <summary>Poll faster the closer the installation is to its cap.</summary>
    private TimeSpan NextInterval()
    {
        long share = Interlocked.Read(ref fairShare);
        if (share == long.MaxValue)
        {
            return RecomputeInterval;
        }

        long total = projectCounts.Values.Sum();
        long limit = share * Math.Max(1, projectCounts.Count);
        return total >= limit * NearLimitFraction ? NearLimitRecomputeInterval : RecomputeInterval;
    }

    private async Task RecomputeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var limit = licenseService.GetLimit(LicenseLimit.MaxTracesPerMonth);
            if (limit == long.MaxValue)
            {
                overQuota = false;
                projectCounts = new Dictionary<Guid, long>();
                Interlocked.Exchange(ref fairShare, long.MaxValue);
                return;
            }

            var now = clock.UtcNow;
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

            var (_, total) = await agentCalls.GetFilteredAsync(
                new AgentCallFilter(From: monthStart),
                page: 1,
                pageSize: 1,
                cancellationToken);

            // Per-project counts reuse the same filtered count the installation total uses, one call
            // per project. Projects are few (they are themselves licence-limited) and this runs on a
            // timer rather than per request, so a handful of counts is cheaper than a bespoke
            // multi-join aggregate — the trace row has no project column, the project is reached
            // through AgentVersion → Agent.
            var allProjects = await projects.GetAllAsync(cancellationToken);
            var counts = new Dictionary<Guid, long>(allProjects.Count);
            foreach (var project in allProjects)
            {
                var (_, projectTotal) = await agentCalls.GetFilteredAsync(
                    new AgentCallFilter(From: monthStart, ProjectId: project.Id),
                    page: 1,
                    pageSize: 1,
                    cancellationToken);
                counts[project.Id] = projectTotal;
            }

            long share = limit / Math.Max(1, allProjects.Count);
            projectCounts = counts;
            Interlocked.Exchange(ref fairShare, share);

            var nowOverQuota = total >= limit;
            bool entering = nowOverQuota && !overQuota;
            overQuota = nowOverQuota;

            if (nowOverQuota)
            {
                await ReportThrottledProjectsAsync(now, counts, share, total, limit, entering, cancellationToken);
            }
            else
            {
                ResetReportingFor(now);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to recompute trace quota");
        }
    }

    /// <summary>
    /// Makes dropping visible: logs at Error (which the operator Error Log captures) and raises one
    /// notification per project as it starts being throttled.
    /// </summary>
    private async Task ReportThrottledProjectsAsync(
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, long> counts,
        long share,
        long total,
        long limit,
        bool entering,
        CancellationToken cancellationToken)
    {
        ResetReportingFor(now);

        if (entering)
        {
            logger.LogError(
                "Monthly trace quota reached ({Total}/{Limit}). Captures are now being dropped for "
                + "projects above their {Share}-trace share of the limit; projects below it continue "
                + "to capture.",
                total, limit, share);
        }

        foreach ((Guid projectId, long count) in counts)
        {
            if (count < share || !reportedProjects.TryAdd(projectId, CurrentMonthKey(now)))
            {
                continue;
            }

            try
            {
                await notifications.NotifyAsync(
                    new NotificationRequest(
                        Kind: NotificationKind.TraceQuotaReached,
                        Severity: NotificationSeverity.Warning,
                        Title: "Trace capture paused for this project",
                        Message:
                            $"This installation has reached its licensed limit of {limit:N0} traces for the month "
                            + $"({total:N0} captured). This project has captured {count:N0}, which is at or above its "
                            + $"{share:N0} share, so new traces for it are not being stored until the month resets or "
                            + "the licence is upgraded. Projects below their share continue to capture.",
                        ProjectId: projectId),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed notification must not stop the guard from enforcing the quota, but it
                // must not be swallowed either — this is the mechanism that makes dropping visible.
                reportedProjects.TryRemove(projectId, out _);
                logger.LogWarning(ex, "Could not raise the trace-quota notification for project {ProjectId}", projectId);
            }
        }
    }

    /// <summary>Clears the per-month "already reported" set when the month rolls over.</summary>
    private void ResetReportingFor(DateTimeOffset now)
    {
        string month = CurrentMonthKey(now);
        if (reportedMonth == month)
        {
            return;
        }

        reportedMonth = month;
        reportedProjects.Clear();
    }

    private static string CurrentMonthKey(DateTimeOffset now) => $"{now.Year:D4}-{now.Month:D2}";
}
