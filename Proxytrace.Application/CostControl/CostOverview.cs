using Proxytrace.Domain.Statistics;

namespace Proxytrace.Application.CostControl;

/// <summary>
/// A single agent's derived spend over the requested window, with the agent's display name
/// resolved so the page needs no second lookup.
/// </summary>
public record AgentCostTotal(Guid AgentId, string AgentName, decimal CostEur);

/// <summary>
/// A single inbound API key's derived spend over the requested window, with the key's name and
/// non-secret prefix resolved so the page can identify it without a second lookup.
/// </summary>
/// <remarks>
/// A null <see cref="ApiKeyId"/> is the unattributed remainder — upstream-key traffic and traces
/// ingested before key attribution existed. The page labels it as such; it is never dropped,
/// because dropping it would make the per-key rows silently fail to sum to the project total.
/// </remarks>
public record ApiKeyCostTotal(Guid? ApiKeyId, string? ApiKeyName, string? KeyPrefix, decimal CostEur);

/// <summary>
/// One configured budget joined with the current month's spend and breach state — everything the
/// Costs page needs to draw a consumption meter without recomputing thresholds client-side.
/// </summary>
public record CostBudgetStatus(
    Guid CostLimitId,
    Guid? AgentId,
    string? AgentName,
    Guid? ApiKeyId,
    string? ApiKeyName,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled,
    decimal MonthToDateSpendEur,
    bool SoftBreached,
    bool HardBreached);

/// <summary>
/// The whole Costs page in one payload: month-to-date and prior-month totals for the management
/// summary, the per-agent series and totals for the window the user selected, and the budget
/// states.
/// </summary>
/// <remarks>
/// <see cref="HasUnpricedEndpoints"/> reports that some traffic in the window ran on an endpoint
/// with no configured price. Those calls contribute nothing to any figure here, so the page must
/// present the numbers as an incomplete estimate rather than a total.
/// </remarks>
public record CostOverview(
    decimal MonthToDateSpendEur,
    decimal PreviousMonthSpendEur,
    IReadOnlyList<AgentCostPoint> Series,
    IReadOnlyList<AgentCostTotal> AgentTotals,
    IReadOnlyList<ApiKeyCostPoint> ApiKeySeries,
    IReadOnlyList<ApiKeyCostTotal> ApiKeyTotals,
    IReadOnlyList<CostBudgetStatus> Budgets,
    bool HasUnpricedEndpoints);
