using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Quickenshtein;

namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// Represents a agent version matcher.
/// </summary>
public interface IAgentVersionMatcher
{
    /// <summary>
    /// Returns the best matching <see cref="IAgentVersion"/> in <paramref name="project"/> for a
    /// new call whose strict fingerprint missed, or null if no candidate clears the similarity
    /// threshold. Tool-sets must match identically on the loose fingerprint; the prompt is
    /// compared by normalized Levenshtein ratio.
    /// </summary>
    Task<IAgentVersion?> FindSimilarVersionAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken);
}

internal sealed class AgentVersionMatcher : IAgentVersionMatcher
{
    private readonly IAgentVersionRepository versions;
    private readonly IAgentCallRepository calls;
    private readonly AgentVersioningOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentVersionMatcher"/> class.
    /// </summary>
    public AgentVersionMatcher(
        IAgentVersionRepository versions,
        IAgentCallRepository calls,
        AgentVersioningOptions options)
    {
        this.versions = versions;
        this.calls = calls;
        this.options = options;
    }

    /// <summary>
    /// Finds the similar version asynchronously.
    /// </summary>
    public async Task<IAgentVersion?> FindSimilarVersionAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken)
    {
        var allCandidates = await versions.GetByLooseFingerprintAsync(project, systemPrompt, tools, cancellationToken);
        if (allCandidates.Count == 0)
        {
            return null;
        }

        // Cap candidates to the most recent N before Levenshtein. Loose-fingerprint collisions can
        // accumulate; bounding the work keeps O(n·m·k) under control.
        var candidates = allCandidates
            .OrderByDescending(v => v.CreatedAt)
            .Take(options.MaxCandidates)
            .ToList();

        int targetLen = systemPrompt.Template.Length;

        var ranked = candidates
            .Where(v => LengthPasses(v.SystemPrompt.Template.Length, targetLen, options.SimilarityThreshold))
            .Select(v => new
            {
                Version = v,
                Ratio = SimilarityRatio(v.SystemPrompt.Template, systemPrompt.Template),
            })
            .Where(x => x.Ratio >= options.SimilarityThreshold)
            .OrderByDescending(x => x.Ratio)
            .ToList();

        if (ranked.Count == 0)
        {
            return null;
        }

        // Tie-break by most-recently-used: highest CreatedAt on calls referencing the version.
        // We don't have a direct call-count query, so just pick the highest ratio; on ties use the
        // most recently created version.
        var top = ranked[0];
        var ties = ranked.Where(x => Math.Abs(x.Ratio - top.Ratio) < 1e-9).ToList();
        if (ties.Count == 1)
        {
            return top.Version;
        }

        return ties.OrderByDescending(t => t.Version.CreatedAt).First().Version;
    }

    /// <summary>
    /// Cheap upper-bound check: any pair of strings with normalized Levenshtein distance ≥
    /// threshold must satisfy <c>1 - |len(a) - len(b)| / max(len) ≥ threshold</c>. Reject obvious
    /// non-matches before the O(n·m) edit-distance computation.
    /// </summary>
    private static bool LengthPasses(int aLen, int bLen, double threshold)
    {
        int max = Math.Max(aLen, bLen);
        if (max == 0)
        {
            return true;
        }
        double upperBound = 1.0 - ((double)Math.Abs(aLen - bLen) / max);
        return upperBound >= threshold;
    }

    /// <summary>
    /// Normalized Levenshtein ratio in [0, 1]. 1 = identical, 0 = completely different.
    /// Backed by the <c>Quickenshtein</c> package (SIMD-accelerated).
    /// </summary>
    private static double SimilarityRatio(string a, string b)
    {
        if (a == b)
        {
            return 1.0;
        }
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0.0;
        }
        int distance = Levenshtein.GetDistance(a, b);
        int max = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / max);
    }
}
