using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.OptimizationProposal;

[UsedImplicitly]
internal class OptimizationProposalRepository :
    AbstractRepository<IOptimizationProposal, OptimizationProposalEntity>,
    IOptimizationProposalRepository
{
    public OptimizationProposalRepository(
        IMapper<IOptimizationProposal, OptimizationProposalEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<IOptimizationProposal>> GetByAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationProposalEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public Task<IReadOnlyList<IOptimizationProposal>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        GetByProjectsAsync([projectId], cancellationToken);

    public async Task<IReadOnlyList<IOptimizationProposal>> GetByProjectsAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<OptimizationProposalEntity>()
            .AsNoTracking()
            .Join(context.Set<AgentEntity>(),
                p => p.Agent,
                a => a.Id,
                (p, a) => new { Proposal = p, Agent = a })
            .Where(x => projectIds.Contains(x.Agent.Project))
            .OrderByDescending(x => x.Proposal.CreatedAt)
            .Select(x => x.Proposal)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IOptimizationProposal?> FindLatestByContentHashAsync(
        Guid agentId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationProposalEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId && e.ContentHash == contentHash)
            .OrderByDescending(e => e.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<IOptimizationProposal>> GetByAgentAndStatusAsync(
        Guid agentId,
        ProposalStatus status,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationProposalEntity>()
            .AsNoTracking()
            .Where(e => e.Agent == agentId && e.Status == status)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<IOptimizationProposal>> GetByStatusAsync(
        ProposalStatus status,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<OptimizationProposalEntity>()
            .AsNoTracking()
            .Where(e => e.Status == status)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }
}
