namespace Proxytrace.Api.Dto.Search;

/// <summary>
/// Data transfer object representing a search results.
/// </summary>
public sealed record SearchResultsDto(IReadOnlyList<SearchHitDto> Hits);
