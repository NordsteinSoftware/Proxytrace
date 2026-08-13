using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.OptimizationTheory;

[UsedImplicitly]
internal class OptimizationTheoryRepository :
    AbstractRepository<IOptimizationTheory, OptimizationTheoryEntity>,
    IOptimizationTheoryRepository
{
    public OptimizationTheoryRepository(
        IMapper<IOptimizationTheory, OptimizationTheoryEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<IOptimizationTheory>> GetByAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<IOptimizationTheory>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                t => t.Agent,
                a => a.Id,
                (t, a) => new { Theory = t, Agent = a })
            .Where(x => x.Agent.Project == projectId)
            .OrderByDescending(x => x.Theory.CreatedAt)
            .Select(x => x.Theory)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IOptimizationTheory?> FindLatestByContentHashAsync(
        Guid agentId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId && e.ContentHash == contentHash)
            .OrderByDescending(e => e.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public Task<int> CountByProjectAndStatusAsync(
        Guid projectId,
        TheoryStatus status,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        return context
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                t => t.Agent,
                a => a.Id,
                (t, a) => new { Theory = t, Agent = a })
            .Where(x => x.Agent.Project == projectId && x.Theory.Status == status)
            .CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IOptimizationTheory>> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Where(e => e.Status == TheoryStatus.Proposed || e.Status == TheoryStatus.Validating)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public Task<int> CountActiveByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        return context
            .Set<OptimizationTheoryEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                t => t.Agent,
                a => a.Id,
                (t, a) => new { Theory = t, Agent = a })
            .Where(x => x.Agent.Project == projectId
                && (x.Theory.Status == TheoryStatus.Proposed || x.Theory.Status == TheoryStatus.Validating))
            .CountAsync(cancellationToken);
    }
}
