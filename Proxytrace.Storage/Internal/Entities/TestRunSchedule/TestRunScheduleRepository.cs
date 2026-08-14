using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.TestRunSchedule;

[UsedImplicitly]
internal class TestRunScheduleRepository : AbstractRepository<ITestRunSchedule, TestRunScheduleEntity>, ITestRunScheduleRepository
{
    public TestRunScheduleRepository(
        IMapper<ITestRunSchedule, TestRunScheduleEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<ITestRunSchedule>> GetByAgentAsync(Guid agentId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunScheduleEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                s => s.Suite,
                t => t.Id,
                (s, t) => new { Schedule = s, Suite = t })
            .Where(x => x.Suite.Agent == agentId)
            .Select(x => x.Schedule)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<ITestRunSchedule>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestRunScheduleEntity>()
            .AsNoTracking()
            .Join(context.Set<TestSuiteEntity>(),
                s => s.Suite,
                t => t.Id,
                (s, t) => new { Schedule = s, Suite = t })
            .Join(context.Set<AgentEntity>(),
                x => x.Suite.Agent,
                a => a.Id,
                (x, a) => new { x.Schedule, Agent = a })
            .Where(x => x.Agent.Project == projectId)
            .Select(x => x.Schedule)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<ITestRunSchedule>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<TestRunScheduleEntity>()
            .AsNoTracking()
            .Where(e => e.IsEnabled && e.NextRunAt <= now)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    protected override async Task UpdateRelationsAsync(
        DbContext context,
        TestRunScheduleEntity storedEntity,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<TestRunScheduleEntity>()
            .Include(s => s.ScheduleEndpoints)
            .FirstOrDefaultAsync(s => s.Id == storedEntity.Id, cancellationToken);

        if (existing is null)
            throw new EntityNotFoundException(storedEntity.Id, typeof(ITestRunSchedule));

        var newIds = storedEntity.ScheduleEndpoints.Select(e => e.EndpointId).ToHashSet();
        var existingIds = existing.ScheduleEndpoints.Select(e => e.EndpointId).ToHashSet();

        var toRemove = existing.ScheduleEndpoints.Where(e => !newIds.Contains(e.EndpointId)).ToList();
        foreach (var item in toRemove)
            context.Set<TestRunScheduleEndpointEntity>().Remove(item);

        var toAdd = newIds.Except(existingIds)
            .Select(id => new TestRunScheduleEndpointEntity { ScheduleId = storedEntity.Id, EndpointId = id });
        foreach (var item in toAdd)
            context.Set<TestRunScheduleEndpointEntity>().Add(item);
    }
}
