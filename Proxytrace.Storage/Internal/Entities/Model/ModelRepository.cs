using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Model;

namespace Proxytrace.Storage.Internal.Entities.Model;

[UsedImplicitly]
internal class ModelRepository : AbstractRepository<IModel, ModelEntity>, IModelRepository
{
    private readonly IModel.CreateNew factory;
    private readonly IAsyncLock locker;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelRepository"/> class.
    /// </summary>
    public ModelRepository(
        IMapper<IModel, ModelEntity> mapper,
        IModel.CreateNew factory,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        IAsyncLock locker,
        IEntityCache<IModel> cache,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
        this.factory = factory;
        this.locker = locker;
    }

    /// <summary>
    /// Gets the or create asynchronously.
    /// </summary>
    public async Task<IModel> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var loweredName = name.ToLowerInvariant();
        using IDisposable lockObj = await locker.LockAsync(loweredName, cancellationToken);
        
        var existing = await context
            .Set<ModelEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name.ToLower() == loweredName, cancellationToken);

        if (existing is not null)
        {
            return await mapper.Map(existing, cancellationToken);
        }

        var model = factory(name: name);
        try
        {
            return await AddAsync(model, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The IAsyncLock only serializes creates within this process; another instance can win
            // the create race and trip the unique index on Name. Re-resolve and return the row the
            // winner inserted (mirrors AgentRepository.GetOrCreateAsync).
            var raced = await contextFactory()
                .Set<ModelEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Name.ToLower() == loweredName, cancellationToken);
            if (raced is not null)
            {
                return await mapper.Map(raced, cancellationToken);
            }
            throw;
        }
    }
}
