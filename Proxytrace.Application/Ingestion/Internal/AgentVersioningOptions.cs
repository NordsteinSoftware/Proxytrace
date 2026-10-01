namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// Configuration for the fuzzy agent-version matcher.
/// </summary>
public sealed record AgentVersioningOptions
{
    /// <summary>
    /// Minimum normalized Levenshtein ratio (0..1) on the system prompt for two versions to be
    /// considered the "same logical agent" when tool shapes match or share a tool name.
    /// </summary>
    public double SimilarityThreshold { get; init; } = 0.85;

    /// <summary>
    /// Maximum number of candidates to evaluate per matching stage. Caps Levenshtein work.
    /// </summary>
    public int MaxCandidates { get; init; } = 32;
}
