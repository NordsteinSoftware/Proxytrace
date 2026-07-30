using Proxytrace.Domain.CostLimitBreach;

namespace Proxytrace.Proxy;

/// <summary>
/// Supplies the active monthly-budget hard blocks of a project for the proxy hot path.
/// Implementations cache per project (including empty lists — most projects have no budget at all)
/// and degrade to an empty list when the blocks cannot be fetched or the license does not include
/// the feature.
/// </summary>
public interface IBudgetBlockProvider
{
    Task<IReadOnlyList<BudgetHardBlock>> GetBlocksAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}
