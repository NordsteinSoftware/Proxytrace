namespace Proxytrace.Proxy;

/// <summary>
/// The budget that is exhausted for a request: which limit, and the agent it is scoped to (null
/// for a project-wide budget). Amounts are deliberately absent — the holder of an ingestion API key
/// is not necessarily entitled to know the organisation's spend figures, so they must appear in no
/// response body.
/// </summary>
public sealed record BudgetBlockMatch(Guid CostLimitId, string? AgentName);

/// <summary>
/// The proxy's monthly-budget enforcement seam: decides whether a request must be rejected because
/// its project (or its named agent) has already crossed a hard spend limit this month. Fail-open by
/// design — a lookup problem must never take LLM traffic down.
/// </summary>
public interface IBudgetBlocker
{
    Task<BudgetBlockMatch?> EvaluateAsync(
        Guid projectId,
        string? agentName,
        CancellationToken cancellationToken);
}
