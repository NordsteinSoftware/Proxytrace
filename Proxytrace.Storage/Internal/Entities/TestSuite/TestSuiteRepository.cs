using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Exceptions;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.TestSuite;

[UsedImplicitly]
internal class TestSuiteRepository : AbstractRepository<ITestSuite, TestSuiteEntity>, ITestSuiteRepository
{
    public TestSuiteRepository(
        IMapper<ITestSuite, TestSuiteEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<ITestSuite>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<TestSuiteEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<ITestSuite>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestSuiteEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                s => s.Agent,
                a => a.Id,
                (s, a) => new { Suite = s, Agent = a })
            .Where(x => x.Agent.Project == projectId)
            .Select(x => x.Suite)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<PagedResult<ITestSuite>> GetByAgentPagedAsync(
        Guid agentId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var query = contextFactory()
            .Set<TestSuiteEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestSuite>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    public Task<PagedResult<ITestSuite>> GetByProjectPagedAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        GetByProjectsPagedAsync([projectId], page, pageSize, cancellationToken);

    public async Task<PagedResult<ITestSuite>> GetByProjectsPagedAsync(
        IReadOnlyCollection<Guid> projectIds,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var context = contextFactory();
        var query = context
            .Set<TestSuiteEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                s => s.Agent,
                a => a.Id,
                (s, a) => new { Suite = s, Agent = a })
            .Where(x => projectIds.Contains(x.Agent.Project))
            .Select(x => x.Suite);

        int total = await query.CountAsync(cancellationToken);
        var stored = await query
            .OrderByDescending(s => s.CreatedAt)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ITestSuite>(await Map(stored, cancellationToken), total, page, pageSize);
    }

    public async Task<Guid?> GetProjectIdByTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();

        // TestCases is a value-converted, serialized JSON Guid[] column (text, not jsonb), so neither
        // provider can translate a membership predicate to SQL. Project only the id array and the
        // owning project via the indexed suite->agent foreign-key join, then test membership in memory.
        // Test suites are a low-volume entity and this is an interactive (non-ingestion) path, so
        // reading the candidate rows is acceptable. See #265.
        var candidates = await context
            .Set<TestSuiteEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                s => s.Agent,
                a => a.Id,
                (s, a) => new { s.TestCases, a.Project })
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c => c.TestCases.Contains(testCaseId))
            .Select(c => (Guid?)c.Project)
            .FirstOrDefault();
    }

    protected override async Task UpdateRelationsAsync(
        StorageDbContext context,
        TestSuiteEntity storedEntity,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<TestSuiteEntity>()
            .Include(s => s.TestSuiteEvaluators)
            .FirstOrDefaultAsync(s => s.Id == storedEntity.Id, cancellationToken);

        if (existing is null)
            throw new EntityNotFoundException(storedEntity.Id, typeof(ITestSuite));

        var newIds = storedEntity.TestSuiteEvaluators.Select(e => e.EvaluatorId).ToHashSet();
        var existingIds = existing.TestSuiteEvaluators.Select(e => e.EvaluatorId).ToHashSet();

        var toRemove = existing.TestSuiteEvaluators.Where(e => !newIds.Contains(e.EvaluatorId)).ToList();
        foreach (var item in toRemove)
            context.Set<TestSuiteEvaluatorEntity>().Remove(item);

        var toAdd = newIds.Except(existingIds)
            .Select(id => new TestSuiteEvaluatorEntity { TestSuiteId = storedEntity.Id, EvaluatorId = id });
        foreach (var item in toAdd)
            context.Set<TestSuiteEvaluatorEntity>().Add(item);
    }
}
