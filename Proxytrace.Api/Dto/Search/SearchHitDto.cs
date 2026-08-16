namespace Proxytrace.Api.Dto.Search;

/// <summary>
/// Data transfer object representing a search hit.
/// </summary>
public sealed record SearchHitDto(
    string Kind,
    Guid EntityId,
    string Title,
    string Snippet,
    double Score,
    IReadOnlyDictionary<string, string> Metadata);
