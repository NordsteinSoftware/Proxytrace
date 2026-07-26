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
    /// Composes the Costs page payload for one project: month-to-date and previous-month totals,
    /// the per-agent cost series and totals over <paramref name="from"/>..<paramref name="to"/>,
    /// and the project's budgets joined with the current month's breach state.
    /// </summary>
    Task<CostOverview> GetCostOverviewAsync(
        Guid projectId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Month-to-date derived spend per (project, agent) across every project — the guard's input.
    /// A project's own total is the sum of its agents' rows, since agent spend also counts toward
    /// the project budget.
    /// </summary>
    Task<IReadOnlyList<ProjectAgentCostStat>> GetMonthToDateSpendAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);
}
