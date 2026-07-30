namespace Proxytrace.Domain.CostLimitBreach;

/// <summary>
/// A scalar projection of an active hard-limit breach, shaped for the proxy's pre-upstream check.
/// Deliberately not a mapped domain entity: mapping would hydrate the project and agent graph the
/// proxy neither has nor needs. <see cref="AgentName"/> is the only pre-upstream attribution signal
/// (the <c>x-proxytrace-agent</c> header), mirroring <c>BlockingDetectorRule</c>.
/// </summary>
/// <summary>
/// One exhausted hard budget the proxy must enforce. Exactly one scope is set: both
/// <paramref name="AgentId"/> and <paramref name="ApiKeyId"/> null means the project-wide budget,
/// which applies to every call.
/// </summary>
/// <param name="AgentName">
/// The scoped agent's name, resolved here because agent matching happens against the
/// <c>x-proxytrace-agent</c> header value. Key matching needs no such lookup — the proxy already
/// holds the authenticating key's id, so it compares ids directly.
/// </param>
public sealed record BudgetHardBlock(
    Guid CostLimitId,
    Guid? AgentId,
    string? AgentName,
    Guid? ApiKeyId = null);

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
