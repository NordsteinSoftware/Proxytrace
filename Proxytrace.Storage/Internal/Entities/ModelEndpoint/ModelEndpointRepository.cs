using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Storage.Internal.Entities.ModelEndpoint;

[UsedImplicitly]
internal class ModelEndpointRepository : ArchivableRepository<IModelEndpoint, ModelEndpointEntity>,
    IModelEndpointRepository
{
    private readonly IModelRepository models;
    private readonly IAsyncLock locker;
    private readonly IModelEndpoint.CreateNew createNewEndpoint;

    public ModelEndpointRepository(
        IMapper<IModelEndpoint, ModelEndpointEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        IModelRepository models,
        IAsyncLock locker,
        IModelEndpoint.CreateNew createNewEndpoint,
        IEntityCache<IModelEndpoint> cache,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
        this.models = models;
        this.locker = locker;
        this.createNewEndpoint = createNewEndpoint;
    }

    // Archive-only: a hard delete would cascade-remove every AgentCall (trace) recorded against this
    // endpoint. Removal goes through ArchiveAsync; RemoveAsync is refused. The FK Restrict in
    // AgentCallConfig is the DB-level backstop.
    protected override bool SupportsHardDelete => false;

    public async Task<IModelEndpoint> GetOrCreateAsync(
        string modelName,
        IModelProvider provider,
        CancellationToken cancellationToken = default)
    {

        IModel modelEntity = await models.GetOrCreateAsync(modelName, cancellationToken);

        using IDisposable lockObj = await locker.LockAsync(provider.Id, cancellationToken);
        var endpointEntity = await contextFactory().Set<ModelEndpointEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Model == modelEntity.Id && e.Provider == provider.Id, cancellationToken);

        if (endpointEntity is not null)
        {
            // Reuse of an archived endpoint (matching traffic arrived again): restore it so it stops
            // being a live-but-hidden zombie that every list query and the UI filter out.
            if (endpointEntity.IsArchived)
            {
                await UnarchiveAsync(endpointEntity.Id, cancellationToken);
                endpointEntity = await contextFactory().Set<ModelEndpointEntity>()
                    .AsNoTracking()
                    .FirstAsync(e => e.Id == endpointEntity.Id, cancellationToken);
            }

            return await mapper.Map(endpointEntity, cancellationToken);
        }

        var endpoint = createNewEndpoint(modelEntity, provider, null, null, null);
        try
        {
            return await AddAsync(endpoint, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The IAsyncLock only serializes creates within this process; another instance can win
            // the create race and trip the (Model, Provider) unique index. Re-resolve and return the
            // row the winner inserted (mirrors AgentRepository.GetOrCreateAsync).
            var raced = await contextFactory().Set<ModelEndpointEntity>()
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Model == modelEntity.Id && e.Provider == provider.Id, cancellationToken);
            if (raced is not null)
            {
                return await mapper.Map(raced, cancellationToken);
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<IModelEndpoint>> GetByProviderAsync(
        Guid providerId,
        CancellationToken cancellationToken = default)
    {
        var entities = await contextFactory().Set<ModelEndpointEntity>()
            .AsNoTracking()
            .Where(e => e.Provider == providerId)
            .ExcludeArchived()
            .ToListAsync(cancellationToken);

        var result = new List<IModelEndpoint>(entities.Count);
        foreach (var entity in entities)
        {
            result.Add(await mapper.Map(entity, cancellationToken));
        }
        return result;
    }
}