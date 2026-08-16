namespace Proxytrace.Domain.Search;

/// <summary>
/// Write side of the search index: adds, removes, and bulk-reindexes documents, flushing buffered
/// writes to the backing store. Called by the application layer after entity mutations and by the
/// admin reindex trigger.
/// </summary>
public interface ISearchIndexer
{
    /// <summary>Indexes or re-indexes a single entity of the given kind in the given project.</summary>
    Task IndexAsync(SearchKind kind, Guid projectId, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Removes the document for the given entity from the index.</summary>
    Task RemoveAsync(SearchKind kind, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Deletes and rebuilds the full index for the given project from the database.</summary>
    Task ReindexProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Flushes any buffered write operations to the backing search store.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
