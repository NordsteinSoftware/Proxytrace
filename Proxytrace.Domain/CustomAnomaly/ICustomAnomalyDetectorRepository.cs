namespace Proxytrace.Domain.CustomAnomaly;

/// <summary>
/// The scalar projection of a blocking detector the proxy evaluates before forwarding a request
/// upstream. Deliberately not a mapped domain entity: mapping would hydrate the hidden judge agent
/// and the scoped-agent graph, which the proxy neither has nor needs — blocking is trigger-match
/// only, scoped by agent <em>name</em> (the <c>x-proxytrace-agent</c> header is the only
/// pre-upstream attribution signal).
/// </summary>
public sealed record BlockingDetectorRule(
    Guid DetectorId,
    string DetectorName,
    IReadOnlyList<AnomalyTrigger> Triggers,
    bool AllAgents,
    IReadOnlyCollection<string> ScopedAgentNames);

/// <summary>
/// Repository for persisting and querying custom anomaly detector entities.
/// </summary>
public interface ICustomAnomalyDetectorRepository : IRepository<ICustomAnomalyDetector>
{
    Task<IReadOnlyList<ICustomAnomalyDetector>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>Enabled detectors of the project — the set the review pipeline evaluates per call.</summary>
    Task<IReadOnlyList<ICustomAnomalyDetector>> GetEnabledByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enabled detectors of the project with <see cref="ICustomAnomalyDetector.BlockUpstream"/> set,
    /// as lean scalar rules — the set the proxy evaluates inline per request.
    /// </summary>
    Task<IReadOnlyList<BlockingDetectorRule>> GetEnabledBlockingRulesByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);
}
