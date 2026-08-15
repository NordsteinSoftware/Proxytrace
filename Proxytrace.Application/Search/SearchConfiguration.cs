namespace Proxytrace.Application.Search;

/// <summary>
/// Configuration for search.
/// </summary>
public sealed record SearchConfiguration
{
    /// <summary>
    /// Gets or sets the index path.
    /// </summary>
    public string IndexPath { get; init; } = "searchindex";
    /// <summary>
    /// Gets or sets the trace retention days.
    /// </summary>
    public int TraceRetentionDays { get; init; } = 30;
    /// <summary>
    /// Gets or sets the pruner interval hours.
    /// </summary>
    public int PrunerIntervalHours { get; init; } = 6;
    /// <summary>
    /// Gets or sets the hits per kind.
    /// </summary>
    public int HitsPerKind { get; init; } = 5;
    /// <summary>
    /// Gets or sets the snippet max chars.
    /// </summary>
    public int SnippetMaxChars { get; init; } = 160;
}
