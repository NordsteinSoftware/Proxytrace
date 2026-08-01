using Proxytrace.Domain.Statistics;

namespace Proxytrace.Application.CostControl;

/// <summary>
/// Read facade for the Costs page, mirroring <c>IDashboardStatistics</c>. It owns the one
/// month-to-date spend implementation that both the page and the periodic
/// <c>CostBudgetGuard</c> use, so a budget can never be evaluated against a different number than
/// the one the user is looking at.
/// </summary>
public interface ICostStatistics
{
    /// <summary>
    /// Composes the Costs page's spend telemetry for one project: month-to-date and previous-month
    /// totals, and the per-agent and per-key cost series and totals over
    /// <paramref name="from"/>..<paramref name="to"/>.
    /// </summary>
    /// <param name="bucket">
    /// The requested series granularity. It is coarsened when the window would exceed the buckets
    /// the chart can render — the effective value comes back on <see cref="CostOverview.Bucket"/>.
    /// </param>
    Task<CostOverview> GetCostOverviewAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One project's budgets joined with the current month's spend and breach state.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="GetCostOverviewAsync"/> on purpose. This is the part of the Costs
    /// page that reacts to a click — every budget create/edit/delete invalidates it — and it needs
    /// one aggregate scan (two when a key-scoped budget exists) rather than the overview's seven. A
    /// project with no budgets configured runs none at all.
    /// </remarks>
    Task<IReadOnlyList<CostBudgetStatus>> GetBudgetStatusAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Month-to-date derived spend per (project, agent) across every project — the guard's input.
    /// A project's own total is the sum of its agents' rows, since agent spend also counts toward
    /// the project budget.
    /// </summary>
    Task<IReadOnlyList<ProjectAgentCostStat>> GetMonthToDateSpendAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Month-to-date derived spend per (project, inbound API key) across every project — the input
    /// for key-scoped budgets. Fetched separately from the per-agent figures so neither aggregate
    /// pays for the other's grouping; the guard only calls this when a key-scoped budget exists.
    /// </summary>
    Task<IReadOnlyList<ProjectApiKeyCostStat>> GetMonthToDateSpendByApiKeyAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);
}
