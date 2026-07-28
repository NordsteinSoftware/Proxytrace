using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.TestRun;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.TestRunGroup;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.TestRun;

[UsedImplicitly]
internal class TestRunRepository : AbstractRepository<ITestRun, TestRunEntity>, ITestRunRepository
{
    public TestRunRepository(
        IMapper<ITestRun, TestRunEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<ITestRun>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Join(context.Set<TestRunGroupEntity>(),
                r => r.Group,
                g => g.Id,
                (r, g) => new { Run = r, Group = g })
            .Join(context.Set<TestSuiteEntity>(),
                x => x.Group.Suite,
                s => s.Id,
                (x, s) => new { x.Run, Suite = s })
            .Where(x => x.Suite.Agent == agentId)
            .Select(x => x.Run)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<PagedResult<ITestRun>> GetByAgentPagedAsync(
        Guid agentId,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Join(context.Set<TestRunGroupEntity>(),
                r => r.Group,
                g => g.Id,
                (r, g) => new { Run = r, Group = g })
            .Join(context.Set<TestSuiteEntity>(),
                x => x.Group.Suite,
                s => s.Id,
                (x, s) => new { x.Run, x.Group, Suite = s })
            .Where(x => x.Suite.Agent == agentId && (includeSystem || !x.Group.IsSystemRun))
            .Select(x => x.Run);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRun>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    public async Task<PagedResult<ITestRun>> GetAllPagedAsync(
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Join(context.Set<TestRunGroupEntity>(),
                r => r.Group,
                g => g.Id,
                (r, g) => new { Run = r, Group = g })
            .Where(x => includeSystem || !x.Group.IsSystemRun)
            .Select(x => x.Run);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRun>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    public async Task<PagedResult<ITestRun>> GetByProjectsPagedAsync(
        IReadOnlyCollection<Guid> projectIds,
        int page,
        int pageSize,
        bool includeSystem = false,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Join(context.Set<TestRunGroupEntity>(),
                r => r.Group,
                g => g.Id,
                (r, g) => new { Run = r, Group = g })
            .Join(context.Set<TestSuiteEntity>(),
                x => x.Group.Suite,
                s => s.Id,
                (x, s) => new { x.Run, x.Group, Suite = s })
            .Join(context.Set<AgentEntity>(),
                x => x.Suite.Agent,
                a => a.Id,
                (x, a) => new { x.Run, x.Group, Agent = a })
            .Where(x => projectIds.Contains(x.Agent.Project) && (includeSystem || !x.Group.IsSystemRun))
            .Select(x => x.Run);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestRun>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    public async Task<IReadOnlyList<ITestRun>> GetByGroupAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Where(r => r.Group == groupId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<ITestRun>> GetByStatusAsync(
        IReadOnlyCollection<TestRunStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        if (statuses.Count == 0) return [];

        var stored = await contextFactory()
            .Set<TestRunEntity>()
            .AsNoTracking()
            .Where(r => statuses.Contains(r.Status))
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetRunIdsByResultIdsAsync(
        IReadOnlyCollection<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        if (resultIds.Count == 0) return new Dictionary<Guid, Guid>();

        var wanted = resultIds.ToHashSet();
        var context = contextFactory();
        // A run's TestResults is a serialized JSON column, so result→run can't be resolved in SQL.
        // Scan a bounded window of the most recent runs (recent evaluations belong to recent runs)
        // and match in memory — the same pattern TestResultRepository uses for recent lookups.
        var recent = await context
            .Set<TestRunEntity>()
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Take(1000)
            .ToListAsync(cancellationToken);

        var map = new Dictionary<Guid, Guid>();
        foreach (var run in recent)
        {
            foreach (var resultId in run.TestResults)
            {
                if (wanted.Contains(resultId))
                    map.TryAdd(resultId, run.Id);
            }
        }
        return map;
    }
}
