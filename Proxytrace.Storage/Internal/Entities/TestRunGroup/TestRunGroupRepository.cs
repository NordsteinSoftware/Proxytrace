using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.TestRunGroup;

[UsedImplicitly]
internal class TestRunGroupRepository : AbstractRepository<ITestRunGroup, TestRunGroupEntity>, ITestRunGroupRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunGroupRepository"/> class.
    /// </summary>
    public TestRunGroupRepository(
        IMapper<ITestRunGroup, TestRunGroupEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Gets the by agent asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestRunGroup>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Where(x => x.Suite.Agent == agentId && !x.Group.IsSystemRun)
            .Select(x => x.Group)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the by statuses asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestRunGroup>> GetByStatusesAsync(
        IReadOnlyCollection<TestRunStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        if (statuses.Count == 0)
            return [];

        var context = contextFactory();
        var stored = await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Where(g => statuses.Contains(g.Status))
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the pending optimization asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestRunGroup>> GetPendingOptimizationAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
            return [];

        // Terminal statuses only — a group still running has not finished producing the evidence the
        // optimizer reads, and will be enqueued normally when it does. Oldest first so a backlog is
        // worked in the order it accumulated. Bounded, so a long-dormant install does not enqueue its
        // entire history (and its entire LLM cost) in one go on the first start after upgrading.
        var context = contextFactory();
        var stored = await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Where(g => g.OptimizationConsideredAt == null
                        && !g.IsSystemRun
                        && (g.Status == TestRunStatus.Completed || g.Status == TestRunStatus.Failed))
            .OrderBy(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestRunGroup>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Join(context.Set<AgentEntity>(),
                gs => gs.Suite.Agent,
                a => a.Id,
                (gs, a) => new { gs.Group, Agent = a })
            .Where(x => x.Agent.Project == projectId && !x.Group.IsSystemRun)
            .Select(x => x.Group)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the by agent paged asynchronously.
    /// </summary>
    public async Task<PagedResult<ITestRunGroup>> GetByAgentPagedAsync(
        Guid agentId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Where(x => x.Suite.Agent == agentId && (includeSystem || !x.Group.IsSystemRun))
            .Select(x => x.Group);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRunGroup>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    /// <summary>
    /// Gets the by project paged asynchronously.
    /// </summary>
    public Task<PagedResult<ITestRunGroup>> GetByProjectPagedAsync(
        Guid projectId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default) =>
        GetByProjectsPagedAsync([projectId], page, pageSize, includeSystem, cancellationToken);

    /// <summary>
    /// Gets the by projects paged asynchronously.
    /// </summary>
    public async Task<PagedResult<ITestRunGroup>> GetByProjectsPagedAsync(
        IReadOnlyCollection<Guid> projectIds,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Join(context.Set<AgentEntity>(),
                gs => gs.Suite.Agent,
                a => a.Id,
                (gs, a) => new { gs.Group, Agent = a })
            .Where(x => projectIds.Contains(x.Agent.Project) && (includeSystem || !x.Group.IsSystemRun))
            .Select(x => x.Group);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRunGroup>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    /// <summary>
    /// Gets the by suite paged asynchronously.
    /// </summary>
    public async Task<PagedResult<ITestRunGroup>> GetBySuitePagedAsync(
        Guid suiteId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        // The group entity carries the suite FK directly, so this needs no join.
        var query = context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Where(g => g.Suite == suiteId && (includeSystem || !g.IsSystemRun));

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(g => g.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRunGroup>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    /// <summary>
    /// Counts the completed since asynchronously.
    /// </summary>
    public async Task<int> CountCompletedSinceAsync(
        Guid agentId,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        return await context
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                g => g.Suite,
                s => s.Id,
                (g, s) => new { Group = g, Suite = s })
            .Where(x => x.Suite.Agent == agentId
                && !x.Group.IsSystemRun
                && x.Group.Status == TestRunStatus.Completed
                && x.Group.CompletedAt != null
                && x.Group.CompletedAt > since)
            .CountAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the by schedule asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestRunGroup>> GetByScheduleAsync(Guid scheduleId, int take, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<TestRunGroupEntity>()
            .AsNoTracking()
            .Where(e => e.ScheduleId == scheduleId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
        return await Map(stored, cancellationToken);
    }
}
