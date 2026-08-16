using System.Runtime.CompilerServices;
using Lucene.Net.Documents;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal.Mappers;

internal abstract class AbstractDocumentMapper<TDomainEntity> : IDocumentMapper 
    where TDomainEntity : class, IDomainEntity, ISearchable
{
    private readonly IRepository<TDomainEntity> repository;
    private readonly ILogger logger;

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public abstract SearchKind Kind { get; }

    /// <summary>
    /// Gets the entity type.
    /// </summary>
    public Type EntityType => typeof(TDomainEntity);

    protected AbstractDocumentMapper(
        IRepository<TDomainEntity> repository,
        ILogger logger)
    {
        this.repository = repository;
        this.logger = logger;
    }
    
    /// <summary>
    /// Builds asynchronously.
    /// </summary>
    public async Task<Document?> BuildAsync(Guid entityId, CancellationToken cancellationToken)
    {
        var entity = await repository.FindAsync(entityId, cancellationToken);
        return entity is null ? null : GetDocument(entity);
    }

    /// <summary>
    /// Builds the all for project asynchronously.
    /// </summary>
    public async IAsyncEnumerable<Document> BuildAllForProjectAsync(Guid projectId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerable<TDomainEntity> all = repository.EnumerateAsync(cancellationToken)
            .Where(x => x.Project.Id == projectId);
        await foreach (var element in all)
        {
            Document? document = null;
            try
            {
                document = GetDocument(element);
            }
            catch(Exception ex)
            {
                logger.LogWarning(ex, "Failed to build document for {Kind} {EntityId}", Kind, element.Id);
            }
            
            if (document is not null)
            {
                yield return document;    
            }
        }
    }
    
    protected abstract Document? GetDocument(TDomainEntity entity);
}
