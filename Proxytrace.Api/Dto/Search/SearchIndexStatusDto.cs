namespace Proxytrace.Api.Dto.Search;

/// <summary>
/// Data transfer object representing a search index status.
/// </summary>
public sealed record SearchIndexStatusDto(
    DateTimeOffset? LastIndexedAt,
    int DocumentCount,
    bool IsReindexing);
