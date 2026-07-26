namespace Proxytrace.Domain.CostLimitBreach;

/// <summary>
/// A scalar projection of an active hard-limit breach, shaped for the proxy's pre-upstream check.
/// Deliberately not a mapped domain entity: mapping would hydrate the project and agent graph the
/// proxy neither has nor needs. <see cref="AgentName"/> is the only pre-upstream attribution signal
/// (the <c>x-proxytrace-agent</c> header), mirroring <c>BlockingDetectorRule</c>.
/// </summary>
public sealed record BudgetHardBlock(Guid CostLimitId, Guid? AgentId, string? AgentName);

public interface ICostLimitBreachRepository : IRepository<ICostLimitBreach>
{
    /// <summary>Every breach recorded for the given calendar month, across all limits.</summary>
    Task<IReadOnlyList<ICostLimitBreach>> GetForMonthAsync(
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops all breach state of one limit, re-arming its alerts. Called when the thresholds are
    /// edited so the next guard tick re-evaluates against the new values.
    /// </summary>
    Task DeleteForLimitAsync(Guid costLimitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The hard blocks the proxy must enforce for a project in the given month: hard breaches of
    /// still-enabled limits, joined with the scoped agent's name for header matching.
    /// </summary>
    Task<IReadOnlyList<BudgetHardBlock>> GetActiveHardBlocksAsync(
        Guid projectId,
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default);
}
