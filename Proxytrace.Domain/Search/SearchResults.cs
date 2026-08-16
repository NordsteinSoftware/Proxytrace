namespace Proxytrace.Domain.Search;

/// <summary>
/// Represents a search results.
/// </summary>
public sealed record SearchResults(IReadOnlyList<SearchHit> Hits);
