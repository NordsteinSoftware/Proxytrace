using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.CostLimit;
using Nordstein.Core.Domain.Events;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

[UsedImplicitly]
internal class CostLimitRepository
    : AbstractRepository<ICostLimit, CostLimitEntity>,
      ICostLimitRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CostLimitRepository"/> class.
    /// </summary>
    public CostLimitRepository(
        IMapper<ICostLimit, CostLimitEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Gets the by project asynchronously.
    /// </summary>
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

    /// <summary>
    /// Gets the all enabled asynchronously.
    /// </summary>
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
