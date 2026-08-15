namespace Proxytrace.Domain.CustomAnomaly;

/// <summary>
/// Repository for persisting and querying custom anomaly result entities.
/// </summary>
public interface ICustomAnomalyResultRepository : IRepository<ICustomAnomalyResult>
{
    /// <summary>
    /// Batch lookup for list enrichment: all results whose call is in
    /// <paramref name="agentCallIds"/>, in one query.
    /// </summary>
    Task<IReadOnlyList<ICustomAnomalyResult>> GetByAgentCallIdsAsync(
        IReadOnlyCollection<Guid> agentCallIds,
        CancellationToken cancellationToken = default);
}
