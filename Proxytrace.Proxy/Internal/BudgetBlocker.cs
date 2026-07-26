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
internal sealed class BudgetBlocker : IBudgetBlocker
{
    private readonly IBudgetBlockProvider blockProvider;

    public BudgetBlocker(IBudgetBlockProvider blockProvider)
    {
        this.blockProvider = blockProvider;
    }

    public async Task<BudgetBlockMatch?> EvaluateAsync(
        Guid projectId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<BudgetHardBlock> blocks = await blockProvider.GetBlocksAsync(projectId, cancellationToken);

        // First match wins: any applicable block stops the call, and which one it was only affects
        // the log line. Unlike RequestBlocker — whose loop body runs the trigger matcher — the
        // predicate here is a pure filter, so it reads as one.
        BudgetHardBlock? block = blocks.FirstOrDefault(candidate => AppliesTo(candidate, agentName));

        return block is null ? null : new BudgetBlockMatch(block.CostLimitId, block.AgentName);
    }

    private static bool AppliesTo(BudgetHardBlock block, string? agentName)
        => block.AgentId is null
           || (agentName is { Length: > 0 }
               && string.Equals(block.AgentName, agentName, StringComparison.OrdinalIgnoreCase));
}
