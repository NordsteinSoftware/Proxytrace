namespace Proxytrace.Domain.Search;

/// <summary>
/// A single ranked result returned by a full-text search query, carrying enough data for the UI to
/// render a result card and navigate to the entity without a second round-trip.
/// </summary>
public sealed record SearchHit(
    /// <summary>The entity type this result belongs to (agent, test suite, etc.).</summary>
    SearchKind Kind,
    /// <summary>Id of the matched entity.</summary>
    Guid EntityId,
    /// <summary>Display title of the matched entity (e.g. agent or suite name).</summary>
    string Title,
    /// <summary>Excerpt from the indexed text showing the match context, truncated to the configured snippet length.</summary>
    string Snippet,
    /// <summary>Relevance score from the search engine; higher is more relevant.</summary>
    double Score,
    /// <summary>Kind-specific key/value annotations (e.g. project id, status) used for display or routing.</summary>
    IReadOnlyDictionary<string, string> Metadata);
