using Proxytrace.Domain.CostLimitBreach;

namespace Proxytrace.Proxy.Internal;

/// <summary>
/// Decides whether a request falls under an exhausted monthly budget. A project-wide block always
/// applies; an agent-scoped block only when the request named its agent via the
/// <c>x-proxytrace-agent</c> header and that name matches (case-insensitive) — the header is the
/// only attribution signal available before ingestion's fingerprint matching, so unattributed
/// traffic is caught by project-level budgets alone. That makes the project-level budget the
/// reliable backstop, exactly as with agent-scoped detector blocking.
/// </summary>
/// <remarks>
/// A key-scoped block is the one scope that cannot be evaded: every proxied request authenticates
/// with a key, so there is no header to omit. The exception is the upstream-key path, where the
/// caller presents the provider's own credentials and no Proxytrace key exists — that traffic
/// carries a null key id and, like header-less traffic, falls to the project budget.
/// </remarks>
internal sealed class BudgetBlocker : IBudgetBlocker
{
    private readonly IBudgetBlockProvider blockProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BudgetBlocker"/> class.
    /// </summary>
    public BudgetBlocker(IBudgetBlockProvider blockProvider)
    {
        this.blockProvider = blockProvider;
    }

    /// <summary>
    /// Evaluates asynchronously.
    /// </summary>
    public async Task<BudgetBlockMatch?> EvaluateAsync(
        Guid projectId,
        string? agentName,
        Guid? apiKeyId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BudgetHardBlock> blocks = await blockProvider.GetBlocksAsync(projectId, cancellationToken);

        // First match wins: any applicable block stops the call, and which one it was only affects
        // the log line. Unlike RequestBlocker — whose loop body runs the trigger matcher — the
        // predicate here is a pure filter, so it reads as one.
        BudgetHardBlock? block = blocks.FirstOrDefault(candidate => AppliesTo(candidate, agentName, apiKeyId));

        return block is null
            ? null
            : new BudgetBlockMatch(block.CostLimitId, block.AgentName, block.ApiKeyId);
    }

    private static bool AppliesTo(BudgetHardBlock block, string? agentName, Guid? apiKeyId)
        => block switch
        {
            // Agent scope: matched against the header value, case-insensitively.
            { AgentId: not null } => agentName is { Length: > 0 }
                                     && string.Equals(block.AgentName, agentName, StringComparison.OrdinalIgnoreCase),

            // Key scope: matched by id. A request with no key (the upstream-key path) must match
            // nothing here — comparing two nulls as equal would read as "this block covers
            // unattributed traffic", which is exactly what a key-scoped budget does not do.
            { ApiKeyId: { } scopedKey } => apiKeyId == scopedKey,

            // Project-wide: applies to every call.
            _ => true,
        };
}
