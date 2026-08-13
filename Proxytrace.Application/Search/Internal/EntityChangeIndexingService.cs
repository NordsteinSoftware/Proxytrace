using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Search.Internal.Mappers;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

/// <summary>
/// Keeps the search index in sync by subscribing to <see cref="IEntityEventService"/>, the
/// single seam every repository write flows through (<c>AbstractRepository.Notify</c>),
/// regardless of which repository interface the caller used.
/// </summary>
/// <remarks>
/// This replaces the old <c>IndexingRepositoryDecorator</c>, which only wrapped the
/// <c>IRepository&lt;T&gt;</c> registration and was therefore bypassed by every write made
/// through a custom repository interface (e.g. <c>IAgentCallRepository</c>) — which is every
/// searchable kind, so incremental indexing never ran.
/// </remarks>
internal sealed class EntityChangeIndexingService : BackgroundService
{
    private readonly IEntityEventService entityEvents;
    private readonly ISearchIndexer indexer;
    private readonly IReadOnlyDictionary<Type, SearchKind> kindByEntityType;
    private readonly ILogger<EntityChangeIndexingService> logger;

    public EntityChangeIndexingService(
        IEntityEventService entityEvents,
        ISearchIndexer indexer,
        IEnumerable<IDocumentMapper> mappers,
        ILogger<EntityChangeIndexingService> logger)
    {
        this.entityEvents = entityEvents;
        this.indexer = indexer;
        this.logger = logger;
        kindByEntityType = mappers
            .GroupBy(m => m.EntityType)
            .ToDictionary(g => g.Key, g => g.First().Kind);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (kindByEntityType.Count == 0)
        {
            logger.LogInformation("No search document mappers registered; entity-change indexing idle.");
            return;
        }

        ChannelReader<EntityChangedEvent> reader = entityEvents.Subscribe(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await reader.WaitToReadAsync(stoppingToken))
                {
                    return;
                }
                while (reader.TryRead(out EntityChangedEvent? evt))
                {
                    await DispatchAsync(evt, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a transient failure kill the subscription loop — that would silently
                // stop all future indexing until restart. Log and keep draining.
                logger.LogError(ex, "Entity-change indexing loop error; continuing.");
            }
        }
    }

    private async Task DispatchAsync(EntityChangedEvent evt, CancellationToken cancellationToken)
    {
        if (!kindByEntityType.TryGetValue(evt.EntityType, out var kind))
        {
            return;
        }
        try
        {
            // The indexer only enqueues here; the actual document build and per-project settings
            // gate happen on the indexing worker, which knows the project from the entity itself.
            if (evt.ChangeType == EntityChangeType.Removed)
            {
                await indexer.RemoveAsync(kind, evt.EntityId, cancellationToken);
            }
            else
            {
                await indexer.IndexAsync(kind, Guid.Empty, evt.EntityId, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue index op for {EntityType} {EntityId}",
                evt.EntityType.Name, evt.EntityId);
        }
    }
}
