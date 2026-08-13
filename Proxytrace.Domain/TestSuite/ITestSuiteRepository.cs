using Nordstein.Core.Domain.Paging;

namespace Proxytrace.Domain.TestSuite;

/// <summary>
/// Repository for <see cref="ITestSuite"/> entities with agent-scoped lookup.
/// </summary>
public interface ITestSuiteRepository : IRepository<ITestSuite>
{
    /// <summary>
    /// Returns all test suites associated with the agent identified by <paramref name="agentId"/>.
    /// </summary>
    Task<IReadOnlyList<ITestSuite>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ITestSuite>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<PagedResult<ITestSuite>> GetByAgentPagedAsync(
        Guid agentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PagedResult<ITestSuite>> GetByProjectPagedAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suites across several projects, paged as one list. Backs an unfiltered list request from a
    /// caller who may read more than one project: the page has to be computed over the union, so
    /// merging per-project pages afterwards would not produce a correct page (#482).
    /// </summary>
    Task<PagedResult<ITestSuite>> GetByProjectsPagedAsync(
        IReadOnlyCollection<Guid> projectIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the id of the project that owns the test case identified by <paramref name="testCaseId"/>
    /// — the project of the agent whose suite references it — or <see langword="null"/> when no suite
    /// references it. A test case carries no project of its own (suites reference test cases by a
    /// serialized JSON id array, with no queryable foreign key), so this is the only reverse path to a
    /// project; the evaluator test bench uses it to confine a caller-supplied test-case id to a project
    /// the caller may access (cross-tenant guard, #265).
    /// </summary>
    Task<Guid?> GetProjectIdByTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default);
}
