using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Security;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Entities.ModelProvider;

[UsedImplicitly]
internal class ModelProviderRepository : ArchivableRepository<IModelProvider, ModelProviderEntity>, IModelProviderRepository
{
    private readonly ISecretIndexer indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelProviderRepository"/> class.
    /// </summary>
    public ModelProviderRepository(
        IMapper<IModelProvider, ModelProviderEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        IEntityCache<IModelProvider> cache,
        AmbientDbContext ambient,
        ISecretIndexer indexer) : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
        this.indexer = indexer;
    }

    // Archive-only: a hard delete would cascade through this provider's endpoints to every AgentCall
    // (trace) recorded against them. Removal goes through ArchiveAsync (which archives the endpoints
    // too); RemoveAsync is refused. The FK Restrict in ModelEndpointConfig is the DB-level backstop.
    protected override bool SupportsHardDelete => false;

    /// <summary>
    /// Finds the by api key asynchronously.
    /// </summary>
    public async Task<IModelProvider?> FindByApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        // The plaintext key is encrypted (non-deterministic) at rest, so match on its deterministic
        // blind-index instead. Intentionally unfiltered so an archived provider that still receives
        // matching traffic keeps resolving, mirroring agent/endpoint attribution.
        //
        // Both schemes are matched in one indexed query: rows written since the index became keyed
        // carry the "hmac1:" form, while rows predating it still carry the bare SHA-256 until the
        // startup backfill upgrades them. Accepting only the keyed form would make every provider on
        // an install that has not finished backfilling fail to authenticate upstream. The two values
        // cannot collide — the keyed one is prefixed — so this cannot match the wrong provider.
        var keyed = indexer.Index(apiKey);
        var legacy = indexer.LegacyIndex(apiKey);
        var entity = await contextFactory()
            .Set<ModelProviderEntity>()
            .AsNoTracking()
            .Where(e => e.ApiKeyLookupHash == keyed || e.ApiKeyLookupHash == legacy)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(entity, cancellationToken);
    }

    /// <summary>
    /// Archiving a provider also archives its endpoints, so the whole provider disappears from
    /// pickers/listings together. The endpoints are only soft-archived — the AgentCall/TestRun rows
    /// that reference them by id are preserved (a hard provider delete would have cascade-removed them).
    /// </summary>
    protected override async Task ArchiveRelationsAsync(
        DbContext context,
        Guid id,
        CancellationToken cancellationToken)
    {
        var endpoints = await context.Set<ModelEndpointEntity>()
            .Where(e => e.Provider == id && !e.IsArchived)
            .ToListAsync(cancellationToken);

        foreach (var endpoint in endpoints)
        {
            context.Entry(endpoint).CurrentValues.SetValues(
                endpoint with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow });
        }
    }
}
