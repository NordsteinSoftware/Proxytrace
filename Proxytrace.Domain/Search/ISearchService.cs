namespace Proxytrace.Domain.Search;

/// <summary>
/// Full-text search over indexed project entities. Provides ranked query results, entity-id lookups
/// scoped to a single kind (for selective retrieval), and a recency feed used by the search UI's
/// "recent" suggestions.
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Returns ranked search hits across all indexed kinds for the given project and query string.
    /// </summary>
    Task<SearchResults> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns up to <paramref name="maxHits"/> entity ids of the given <paramref name="kind"/>
    /// matching the query, used when only ids (not full hit payloads) are needed.
    /// </summary>
    Task<IReadOnlyList<Guid>> SearchEntityIdsAsync(
        Guid projectId,
        string query,
        SearchKind kind,
        int maxHits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <paramref name="limit"/> most recently indexed documents of the given kinds,
    /// used to populate "recent" suggestions before the user types a query.
    /// </summary>
    Task<SearchResults> GetRecentAsync(
        Guid projectId,
        IReadOnlyList<SearchKind> kinds,
        int limit,
        CancellationToken cancellationToken = default);
}
