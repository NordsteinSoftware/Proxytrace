using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.Domain.Events;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyResult;

[UsedImplicitly]
internal class CustomAnomalyResultRepository
    : AbstractRepository<ICustomAnomalyResult, CustomAnomalyResultEntity>,
      ICustomAnomalyResultRepository
{
    public CustomAnomalyResultRepository(
        IMapper<ICustomAnomalyResult, CustomAnomalyResultEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<ICustomAnomalyResult>> GetByAgentCallIdsAsync(
        IReadOnlyCollection<Guid> agentCallIds,
        CancellationToken cancellationToken = default)
    {
        if (agentCallIds.Count == 0)
            return [];

        var stored = await contextFactory()
            .Set<CustomAnomalyResultEntity>()
            .AsNoTracking()
            .Where(e => agentCallIds.Contains(e.AgentCallId))
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }
}
