namespace Proxytrace.Domain.OptimizationProposal;

/// <summary>
/// Repository for <see cref="IOptimizationProposal"/> entities.
/// </summary>
public interface IOptimizationProposalRepository : IRepository<IOptimizationProposal>
{
    /// <summary>
    /// Returns all proposals targeting the specified agent version, ordered by creation date descending.
    /// </summary>
    Task<IReadOnlyList<IOptimizationProposal>> GetByAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IOptimizationProposal>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Proposals across several projects as one list, newest first. Backs an unfiltered list request
    /// from a caller who may read more than one project (#482), so the set is applied in SQL rather
    /// than by concatenating per-project reads.
    /// </summary>
    Task<IReadOnlyList<IOptimizationProposal>> GetByProjectsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most-recently-updated proposal for the given agent with the specified
    /// <see cref="IOptimizationProposal.ContentHash"/>, or null if none exists.
    /// Used by the optimizer to detect duplicate suggestions.
    /// </summary>
    Task<IOptimizationProposal?> FindLatestByContentHashAsync(
        Guid agentId,
        string contentHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all proposals for the agent in the given status, ordered by creation date descending.
    /// Used by adoption detection to find promoted proposals awaiting adoption.
    /// </summary>
    Task<IReadOnlyList<IOptimizationProposal>> GetByAgentAndStatusAsync(
        Guid agentId,
        ProposalStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all proposals in the given status across all agents.
    /// Used by the adoption-detection startup sweep.
    /// </summary>
    Task<IReadOnlyList<IOptimizationProposal>> GetByStatusAsync(
        ProposalStatus status,
        CancellationToken cancellationToken = default);
}
