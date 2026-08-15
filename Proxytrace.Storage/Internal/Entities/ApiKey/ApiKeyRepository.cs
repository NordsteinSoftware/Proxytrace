using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Security;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Nordstein.Core.Domain.Events;

namespace Proxytrace.Storage.Internal.Entities.ApiKey;

[UsedImplicitly]
internal class ApiKeyRepository : AbstractRepository<IApiKey, ApiKeyEntity>, IApiKeyRepository
{
    private readonly ISecretHasher hasher;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyRepository"/> class.
    /// </summary>
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

    /// <summary>
    /// Finds the by key asynchronously.
    /// </summary>
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

    /// <summary>
    /// Gets the by provider asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<IApiKey>> GetByProviderAsync(Guid providerId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.Provider == providerId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the by project asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<IApiKey>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the key names by owner asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetKeyNamesByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
        // Projects the name column only — no mapping, so this never materializes a key entity (and
        // never resolves its project/provider/owner graph) just to answer "does this user own any?".
        => await contextFactory()
            .Set<ApiKeyEntity>()
            .AsNoTracking()
            .Where(e => e.Owner == ownerId)
            .OrderBy(e => e.Name)
            .Select(e => e.Name)
            .ToListAsync(cancellationToken);
}
