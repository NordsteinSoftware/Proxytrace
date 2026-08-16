namespace Proxytrace.Application.Search;

/// <summary>
/// Configuration for the Lucene-backed full-text search index, bound from the 'Search' config section.
/// </summary>
public sealed record SearchConfiguration
{
    /// <summary>
    /// Filesystem directory where the Lucene index segments are written.
    /// </summary>
    public string IndexPath { get; init; } = "searchindex";

    /// <summary>
    /// Trace entries older than this are pruned from the search index independently of DB retention.
    /// </summary>
    public int TraceRetentionDays { get; init; } = 30;

    /// <summary>
    /// How often the background pruner runs to remove stale trace index entries.
    /// </summary>
    public int PrunerIntervalHours { get; init; } = 6;

    /// <summary>
    /// Maximum results returned per entity kind (traces, agents, suites) in a single search response.
    /// </summary>
    public int HitsPerKind { get; init; } = 5;

    /// <summary>
    /// Maximum character length of the highlighted text snippet attached to each search hit.
    /// </summary>
    public int SnippetMaxChars { get; init; } = 160;
}
