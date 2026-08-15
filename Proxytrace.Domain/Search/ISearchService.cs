namespace Proxytrace.Domain.Search;

/// <summary>
/// Service that provides search functionality.
/// </summary>
public interface ISearchService
{
    Task<SearchResults> SearchAsync(Guid projectId, string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> SearchEntityIdsAsync(
        Guid projectId,
        string query,
        SearchKind kind,
        int maxHits,
        CancellationToken cancellationToken = default);

    Task<SearchResults> GetRecentAsync(
        Guid projectId,
        IReadOnlyList<SearchKind> kinds,
        int limit,
        CancellationToken cancellationToken = default);
}
