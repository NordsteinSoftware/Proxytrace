using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

[UsedImplicitly]
internal class CustomAnomalyDetectorRepository
    : AbstractRepository<ICustomAnomalyDetector, CustomAnomalyDetectorEntity>,
      ICustomAnomalyDetectorRepository
{
    private readonly ISerializer serializer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyDetectorRepository"/> class.
    /// </summary>
    public CustomAnomalyDetectorRepository(
        IMapper<ICustomAnomalyDetector, CustomAnomalyDetectorEntity> mapper,
        ISerializer serializer,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.serializer = serializer;
    }

    /// <summary>
    /// Gets the by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ICustomAnomalyDetector>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<CustomAnomalyDetectorEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the enabled by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ICustomAnomalyDetector>> GetEnabledByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<CustomAnomalyDetectorEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId && e.IsEnabled)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the enabled blocking rules by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<BlockingDetectorRule>> GetEnabledBlockingRulesByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Scalar projection only — no domain mapping. Mapping would hydrate the hidden judge agent
        // and scoped-agent graph, which the proxy (the caller) stubs out; blocking needs patterns
        // and scoped agent NAMES, nothing more.
        var context = contextFactory();
        var stored = await context.Set<CustomAnomalyDetectorEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId && e.IsEnabled && e.BlockUpstream)
            .Select(e => new { e.Id, e.Name, e.Triggers, e.AllAgents })
            .ToListAsync(cancellationToken);

        if (stored.Count == 0)
            return [];

        var scopedDetectorIds = stored.Where(e => !e.AllAgents).Select(e => e.Id).ToList();
        Dictionary<Guid, List<string>> namesByDetector = [];
        if (scopedDetectorIds.Count > 0)
        {
            var rows = await context.Set<CustomAnomalyDetectorAgentEntity>()
                .AsNoTracking()
                .Where(j => scopedDetectorIds.Contains(j.DetectorId))
                .Join(
                    context.Set<AgentEntity>().AsNoTracking(),
                    j => j.AgentId,
                    a => a.Id,
                    (j, a) => new { j.DetectorId, AgentName = a.Name })
                .ToListAsync(cancellationToken);

            namesByDetector = rows
                .GroupBy(r => r.DetectorId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.AgentName).ToList());
        }

        return stored
            .Select(e => new BlockingDetectorRule(
                DetectorId: e.Id,
                DetectorName: e.Name,
                Triggers: serializer.DeserializeRequired<List<AnomalyTrigger>>(e.Triggers),
                AllAgents: e.AllAgents,
                ScopedAgentNames: namesByDetector.TryGetValue(e.Id, out var names) ? names : []))
            .ToList();
    }

    protected override async Task UpdateRelationsAsync(
        DbContext context,
        CustomAnomalyDetectorEntity storedEntity,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<CustomAnomalyDetectorEntity>()
            .Include(d => d.ScopedAgents)
            .FirstOrDefaultAsync(d => d.Id == storedEntity.Id, cancellationToken);

        if (existing is null)
            throw new EntityNotFoundException(storedEntity.Id, typeof(ICustomAnomalyDetector));

        var newIds = storedEntity.ScopedAgents.Select(e => e.AgentId).ToHashSet();
        var existingIds = existing.ScopedAgents.Select(e => e.AgentId).ToHashSet();

        var toRemove = existing.ScopedAgents.Where(e => !newIds.Contains(e.AgentId)).ToList();
        foreach (var item in toRemove)
            context.Set<CustomAnomalyDetectorAgentEntity>().Remove(item);

        var toAdd = newIds.Except(existingIds)
            .Select(id => new CustomAnomalyDetectorAgentEntity { DetectorId = storedEntity.Id, AgentId = id });
        foreach (var item in toAdd)
            context.Set<CustomAnomalyDetectorAgentEntity>().Add(item);
    }
}
