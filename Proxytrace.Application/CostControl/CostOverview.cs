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
/// The Costs page's spend telemetry: month-to-date and prior-month totals for the management
/// summary, plus the per-agent and per-key series and totals for the window the user selected.
/// </summary>
/// <remarks>
/// <para>
/// Budget state is deliberately <b>not</b> here — it is its own read
/// (<see cref="ICostStatistics.GetBudgetStatusAsync"/>). This payload costs seven aggregate scans
/// of the highest-volume table; the budget list costs one or two, and it is the part that has to
/// react to a click. Folding them together made every budget edit re-derive the whole page.
/// </para>
/// <para>
/// <see cref="HasUnpricedEndpoints"/> reports that some traffic in the window ran on an endpoint
/// with no configured price. Those calls contribute nothing to any figure here, so the page must
/// present the numbers as an incomplete estimate rather than a total.
/// </para>
/// </remarks>
/// <param name="Bucket">
/// The granularity the series was actually aggregated at, which is the requested one coarsened when
/// the window would otherwise produce more buckets than the chart renders. The client must label
/// and densify against this, not against what it asked for.
/// </param>
public record CostOverview(
    decimal MonthToDateSpendEur,
    decimal PreviousMonthSpendEur,
    IReadOnlyList<AgentCostPoint> Series,
    IReadOnlyList<AgentCostTotal> AgentTotals,
    IReadOnlyList<ApiKeyCostPoint> ApiKeySeries,
    IReadOnlyList<ApiKeyCostTotal> ApiKeyTotals,
    bool HasUnpricedEndpoints,
    StatisticsBucket Bucket);
