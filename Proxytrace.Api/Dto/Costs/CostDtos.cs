namespace Proxytrace.Api.Dto.Costs;

/// <summary>
/// A configured monthly budget as the client sees it. Amounts are EUR; a null threshold means
/// that threshold is not set.
/// </summary>
public record CostLimitDto(
    Guid Id,
    Guid ProjectId,
    Guid? AgentId,
    string? AgentName,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Creates a budget. <c>AgentId</c> null creates (or conflicts with) the project-wide budget;
/// a value creates the override for that agent.
/// </summary>
public record CreateCostLimitRequest(
    Guid ProjectId,
    Guid? AgentId,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled = true);

/// <summary>
/// Updates a budget's thresholds. The scope (project/agent) is immutable — retarget by deleting
/// and recreating. Saving clears the budget's breach state so the next guard tick re-evaluates
/// against the new thresholds.
/// </summary>
public record UpdateCostLimitRequest(
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled);

public record AgentCostPointDto(DateTimeOffset BucketStart, Guid AgentId, decimal CostEur);

public record AgentCostTotalDto(Guid AgentId, string AgentName, decimal CostEur);

public record CostBudgetStatusDto(
    Guid CostLimitId,
    Guid? AgentId,
    string? AgentName,
    decimal? SoftLimitEur,
    decimal? HardLimitEur,
    bool Enabled,
    decimal MonthToDateSpendEur,
    bool SoftBreached,
    bool HardBreached);

/// <summary>
/// The Costs page payload. <c>HasUnpricedEndpoints</c> reports that some traffic in the window ran
/// on an endpoint with no configured price and therefore contributes nothing to these figures —
/// the estimate is incomplete, not merely approximate.
/// </summary>
public record CostOverviewDto(
    decimal MonthToDateSpendEur,
    decimal PreviousMonthSpendEur,
    IReadOnlyList<AgentCostPointDto> Series,
    IReadOnlyList<AgentCostTotalDto> AgentTotals,
    IReadOnlyList<CostBudgetStatusDto> Budgets,
    bool HasUnpricedEndpoints,
    string Bucket);
