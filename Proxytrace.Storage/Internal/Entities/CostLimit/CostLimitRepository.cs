using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Events;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

[UsedImplicitly]
internal class CostLimitRepository
    : AbstractRepository<ICostLimit, CostLimitEntity>,
      ICostLimitRepository
{
    public CostLimitRepository(
        IMapper<ICostLimit, CostLimitEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    public async Task<IReadOnlyList<ICostLimit>> GetByProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<CostLimitEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<ICostLimit>> GetAllEnabledAsync(CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<CostLimitEntity>()
            .AsNoTracking()
            .Where(e => e.Enabled)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }
}
