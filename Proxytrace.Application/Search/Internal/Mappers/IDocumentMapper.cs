using Lucene.Net.Documents;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal.Mappers;

internal interface IDocumentMapper
{
    SearchKind Kind { get; }

    /// <summary>
    /// The domain entity interface type this mapper indexes (e.g. <c>IAgentCall</c>). Used to
    /// route <see cref="Nordstein.Core.Domain.Events.EntityChangedEvent"/>s (keyed by entity type)
    /// to the right <see cref="SearchKind"/>.
    /// </summary>
    Type EntityType { get; }

    Task<Document?> BuildAsync(Guid entityId, CancellationToken cancellationToken);
    IAsyncEnumerable<Document> BuildAllForProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
