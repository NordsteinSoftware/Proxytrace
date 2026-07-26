namespace Proxytrace.Application.CostControl;

/// <summary>
/// Tuning for the periodic cost-budget guard, bound from the <c>CostControl</c> configuration
/// section.
/// </summary>
public record CostControlOptions
{
    /// <summary>
    /// How often month-to-date spend is recomputed and compared against the configured budgets.
    /// This interval, plus the proxy's block-cache TTL, is the worst-case overshoot past a hard
    /// limit — an inherent cost of recomputing spend periodically instead of on every call.
    /// </summary>
    public int GuardIntervalSeconds { get; init; } = 300;
}
