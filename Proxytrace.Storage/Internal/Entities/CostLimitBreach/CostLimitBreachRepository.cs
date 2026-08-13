using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Nordstein.Core.Domain.Events;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.CostLimitBreach;

[UsedImplicitly]
internal class CostLimitBreachRepository
    : AbstractRepository<ICostLimitBreach, CostLimitBreachEntity>,
      ICostLimitBreachRepository
{
    public CostLimitBreachRepository(
        IMapper<ICostLimitBreach, CostLimitBreachEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<FiredThreshold>> GetFiredThresholdsAsync(
        DateTimeOffset monthStart,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        // Scalar projection only — no domain mapping, for the same reason as
        // GetActiveHardBlocksAsync below: Map resolves the full ICostLimit per row, and
        // CostLimitEntity is not cacheable, so mapping N breaches meant N serial round trips to
        // recover an id the projection already carries.
        var context = contextFactory();

        IQueryable<CostLimitBreachEntity> month = context.Set<CostLimitBreachEntity>()
            .AsNoTracking()
            .Where(b => b.MonthStart == monthStart);

        // The project filter is a join rather than a column on the breach: the tenant lives on the
        // limit. Unscoped is the guard's cross-tenant read and nothing else.
        IQueryable<FiredThreshold> query = projectId is { } scopedProjectId
            ? month
                .Join(
                    context.Set<Entities.CostLimit.CostLimitEntity>().AsNoTracking(),
                    b => b.CostLimit,
                    l => l.Id,
                    (b, l) => new { Breach = b, Limit = l })
                .Where(x => x.Limit.Project == scopedProjectId)
                .Select(x => new FiredThreshold(x.Breach.CostLimit, x.Breach.Threshold))
            : month.Select(b => new FiredThreshold(b.CostLimit, b.Threshold));

        return await query.ToListAsync(cancellationToken);
    }

    public async Task DeleteForLimitAsync(Guid costLimitId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<CostLimitBreachEntity>().Where(e => e.CostLimit == costLimitId);

        if (context.Database.IsRelational())
        {
            await query.ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            // The in-memory provider does not support ExecuteDelete — materialize then remove.
            var toRemove = await query.ToListAsync(cancellationToken);
            context.Set<CostLimitBreachEntity>().RemoveRange(toRemove);
            await context.SaveChangesAsync(cancellationToken);
        }

        Notify(costLimitId, EntityChangeType.Removed);
    }

    public async Task<IReadOnlyList<BudgetHardBlock>> GetActiveHardBlocksAsync(
        Guid projectId,
        DateTimeOffset monthStart,
        CancellationToken cancellationToken = default)
    {
        // Scalar projection only — no domain mapping. Mapping would hydrate the project and agent
        // graph, which the proxy (the caller) stubs out; enforcement needs the scoped agent NAME
        // and nothing else. A limit disabled after its breach fired stops blocking immediately,
        // because the join filters on Enabled rather than on the breach row alone.
        var context = contextFactory();

        var rows = await context.Set<CostLimitBreachEntity>()
            .AsNoTracking()
            .Where(b => b.MonthStart == monthStart && b.Threshold == CostThreshold.Hard)
            .Join(
                context.Set<Entities.CostLimit.CostLimitEntity>().AsNoTracking(),
                b => b.CostLimit,
                l => l.Id,
                (b, l) => new { Breach = b, Limit = l })
            .Where(x => x.Limit.Enabled && x.Limit.Project == projectId && x.Limit.HardLimitEur != null)
            .Select(x => new { x.Limit.Id, x.Limit.Agent, x.Limit.ApiKey })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return [];

        var agentIds = rows.Where(r => r.Agent != null).Select(r => r.Agent).ToList();
        Dictionary<Guid, string> agentNames = [];
        if (agentIds.Count > 0)
        {
            agentNames = await context.Set<AgentEntity>()
                .AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);
        }

        return rows
            .Select(r => new BudgetHardBlock(
                CostLimitId: r.Id,
                AgentId: r.Agent,
                AgentName: r.Agent is { } id && agentNames.TryGetValue(id, out var name) ? name : null,
                // No name lookup for the key: enforcement compares the authenticating key's id, so
                // the name would only ever be dead weight on the hot path.
                ApiKeyId: r.ApiKey))
            .ToList();
    }
}
