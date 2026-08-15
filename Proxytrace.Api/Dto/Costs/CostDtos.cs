namespace Proxytrace.Api.Dto.Costs;

/// <summary>
/// A configured monthly budget as the client sees it. Amounts are EUR; a null threshold means
/// that threshold is not set. At most one of <c>AgentId</c> / <c>ApiKeyId</c> is set — both null
/// means the project-wide budget.
/// </summary>
public record CostLimitDto(
    Guid Id,
    Guid ProjectId,
    Guid? AgentId,
    string? AgentName,
    Guid? ApiKeyId,
    string? ApiKeyName,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Creates a budget. Both <c>AgentId</c> and <c>ApiKeyId</c> null creates (or conflicts with) the
/// project-wide budget; setting one creates the override for that agent or that inbound API key.
/// Setting both is rejected — a budget has exactly one scope.
/// </summary>
public record CreateCostLimitRequest(
    Guid ProjectId,
    Guid? AgentId,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled = true,
    Guid? ApiKeyId = null);

/// <summary>
/// Updates a budget's thresholds. The scope (project/agent/key) is immutable — retarget by deleting
/// and recreating. Saving clears the budget's breach state so the next guard tick re-evaluates
/// against the new thresholds.
/// </summary>
public record UpdateCostLimitRequest(
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled);

/// <summary>
/// Data transfer object representing a agent cost point.
/// </summary>
public record AgentCostPointDto(DateTimeOffset BucketStart, Guid AgentId, decimal CostEur);

/// <summary>
/// Data transfer object representing a agent cost total.
/// </summary>
public record AgentCostTotalDto(Guid AgentId, string AgentName, decimal CostEur);

/// <summary>
/// One bucket of spend attributed to one inbound API key. <c>ApiKeyId</c> is null for the
/// unattributed series — traffic authenticated with the provider's own upstream key, and traces
/// ingested before key attribution existed.
/// </summary>
public record ApiKeyCostPointDto(DateTimeOffset BucketStart, Guid? ApiKeyId, decimal CostEur);

/// <summary>
/// Month-to-date spend attributed to one inbound API key. A null <c>ApiKeyId</c> is the
/// unattributed remainder; it is reported explicitly rather than dropped so the per-key figures
/// always sum to the project total.
/// </summary>
public record ApiKeyCostTotalDto(Guid? ApiKeyId, string? ApiKeyName, string? KeyPrefix, decimal CostEur);

/// <summary>
/// One budget joined with this month's spend and breach state — the payload of
/// <c>GET /api/cost-limits/status</c>. Kept out of <see cref="CostOverviewDto"/> so a budget change
/// re-reads one or two aggregates instead of the whole page's telemetry.
/// </summary>
public record CostBudgetStatusDto(
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
/// The Costs page's spend telemetry. <c>HasUnpricedEndpoints</c> reports that some traffic in the
/// window ran on an endpoint with no configured price and therefore contributes nothing to these
/// figures — the estimate is incomplete, not merely approximate.
/// </summary>
/// <param name="Bucket">
/// The granularity the series was actually aggregated at. It is the requested bucket coarsened when
/// the window would produce more cells than the chart draws, so the client must label and densify
/// against this value rather than against what it asked for.
/// </param>
public record CostOverviewDto(
    decimal MonthToDateSpendEur,
    decimal PreviousMonthSpendEur,
    IReadOnlyList<AgentCostPointDto> Series,
    IReadOnlyList<AgentCostTotalDto> AgentTotals,
    IReadOnlyList<ApiKeyCostPointDto> ApiKeySeries,
    IReadOnlyList<ApiKeyCostTotalDto> ApiKeyTotals,
    bool HasUnpricedEndpoints,
    string Bucket);
