namespace Proxytrace.Domain.CostLimit;

public interface ICostLimitRepository : IRepository<ICostLimit>
{
    /// <summary>The project's limits — the project-wide one plus every agent override.</summary>
    Task<IReadOnlyList<ICostLimit>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every enabled limit across all projects — the working set the periodic budget guard
    /// evaluates. Returns empty when no budgets are configured, which lets the guard skip its
    /// spend query entirely.
    /// </summary>
    Task<IReadOnlyList<ICostLimit>> GetAllEnabledAsync(CancellationToken cancellationToken = default);
}
