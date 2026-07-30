using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Events;

namespace Proxytrace.Storage.Internal.Entities.ApiKey;

[UsedImplicitly]
internal class ApiKeyRepository : AbstractRepository<IApiKey, ApiKeyEntity>, IApiKeyRepository
{
    private readonly ISecretHasher hasher;

    public ApiKeyRepository(
        IMapper<IApiKey, ApiKeyEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        ISecretHasher hasher) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.hasher = hasher;
    }

    public async Task<IApiKey?> FindByKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        // The key is stored as a hash; match on the hash of the presented raw key.
        var keyHash = hasher.Hash(key);
        var entity = await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.KeyHash == keyHash)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(entity, cancellationToken);
    }

    public async Task<IReadOnlyList<IApiKey>> GetByProviderAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.Provider == providerId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<IApiKey>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }
}
