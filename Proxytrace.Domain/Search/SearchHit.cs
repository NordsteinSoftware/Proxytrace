namespace Proxytrace.Domain.Search;

/// <summary>
/// Represents a search hit.
/// </summary>
public sealed record SearchHit(
    SearchKind Kind,
    Guid EntityId,
    string Title,
    string Snippet,
    double Score,
    IReadOnlyDictionary<string, string> Metadata);
