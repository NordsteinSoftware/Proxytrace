namespace Proxytrace.Proxy;

/// <summary>
/// The budget that is exhausted for a request: which limit, and what it is scoped to (both names
/// null for a project-wide budget). Amounts are deliberately absent — the holder of an ingestion API
/// key is not necessarily entitled to know the organisation's spend figures, so they must appear in
/// no response body.
/// </summary>
public sealed record BudgetBlockMatch(Guid CostLimitId, string? AgentName, Guid? ApiKeyId = null);

/// <summary>
/// The proxy's monthly-budget enforcement seam: decides whether a request must be rejected because
/// its project, its named agent, or the API key it authenticated with has already crossed a hard
/// spend limit this month. Fail-open by design — a lookup problem must never take LLM traffic down.
/// </summary>
public interface IBudgetBlocker
{
    /// <summary>
    /// Evaluates every scope against one request. <paramref name="agentName"/> is the
    /// <c>x-proxytrace-agent</c> header value (absent for unattributed traffic);
    /// <paramref name="apiKeyId"/> is the authenticating Proxytrace key, null on the upstream-key
    /// path.
    /// </summary>
    Task<BudgetBlockMatch?> EvaluateAsync(
        Guid projectId,
        string? agentName,
        Guid? apiKeyId,
        CancellationToken cancellationToken);
}
