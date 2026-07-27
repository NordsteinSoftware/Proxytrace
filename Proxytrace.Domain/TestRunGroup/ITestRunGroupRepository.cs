using Proxytrace.Domain.Paging;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.TestRunGroup;

public interface ITestRunGroupRepository : IRepository<ITestRunGroup>
{
    Task<IReadOnlyList<ITestRunGroup>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every group whose <see cref="ITestRunGroup.Status"/> is one of <paramref name="statuses"/>.
    /// Used at startup to find groups left non-terminal by a previous shutdown. Filters in SQL.
    /// </summary>
    Task<IReadOnlyList<ITestRunGroup>> GetByStatusesAsync(
        IReadOnlyCollection<TestRunStatus> statuses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns terminal, non-system groups the optimizer has not considered yet, oldest first.
    /// </summary>
    /// <remarks>
    /// Backs the optimizer's restart recovery. Its queue is an in-process channel, so a restart
    /// discarded whatever was still in it — a deploy during a scheduled-run window silently dropped
    /// that night's optimization. System runs are excluded because they are the optimizer's own A/B
    /// runs and must never re-trigger it. Filters in SQL over the
    /// (OptimizationConsideredAt, IsSystemRun, Status) index.
    /// </remarks>
    Task<IReadOnlyList<ITestRunGroup>> GetPendingOptimizationAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ITestRunGroup>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<PagedResult<ITestRunGroup>> GetByAgentPagedAsync(
        Guid agentId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ITestRunGroup>> GetByProjectPagedAsync(
        Guid projectId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run groups across several projects, paged as one list. Backs an unfiltered list request from
    /// a caller who may read more than one project: the page has to be computed over the union, so
    /// merging per-project pages afterwards would not produce a correct page (#482).
    /// </summary>
    Task<PagedResult<ITestRunGroup>> GetByProjectsPagedAsync(
        IReadOnlyCollection<Guid> projectIds,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default);

    /// <summary>Run groups for a single suite, newest first — backs the suite's run-history view.</summary>
    Task<PagedResult<ITestRunGroup>> GetBySuitePagedAsync(
        Guid suiteId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts completed test-run groups against the given agent whose <c>CompletedAt</c>
    /// is strictly after the supplied threshold.
    /// </summary>
    Task<int> CountCompletedSinceAsync(
        Guid agentId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);

    /// <summary>Most recent groups (newest first) created by the given schedule.</summary>
    Task<IReadOnlyList<ITestRunGroup>> GetByScheduleAsync(Guid scheduleId, int take, CancellationToken cancellationToken = default);
}
