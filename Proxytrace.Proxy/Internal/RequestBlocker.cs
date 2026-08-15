using Proxytrace.Domain.CustomAnomaly;

namespace Proxytrace.Proxy.Internal;

/// <summary>
/// Evaluates a project's blocking detector rules against the raw request body. Matching runs on the
/// JSON text as sent (no parsing), so every field — system prompt, messages, tool definitions — is
/// covered; patterns spanning characters JSON escapes (quotes, newlines) must use their escaped
/// form. Scope: <c>AllAgents</c> rules always apply; agent-scoped rules only when the request named
/// its agent via the <c>x-proxytrace-agent</c> header and that name matches a scoped agent
/// (case-insensitive) — the header is the only attribution signal available before ingestion's
/// fingerprint matching, and an unattributed request is forwarded (the post-hoc review still
/// catches it).
/// </summary>
internal sealed class RequestBlocker : IRequestBlocker
{
    private readonly IBlockingRuleProvider ruleProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestBlocker"/> class.
    /// </summary>
    public RequestBlocker(IBlockingRuleProvider ruleProvider)
    {
        this.ruleProvider = ruleProvider;
    }

    /// <summary>
    /// Evaluates asynchronously.
    /// </summary>
    public async Task<BlockedRequestMatch?> EvaluateAsync(
        Guid projectId,
        string? agentName,
        string requestBody,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(requestBody))
        {
            return null;
        }

        var rules = await ruleProvider.GetRulesAsync(projectId, cancellationToken);

        foreach (var rule in rules)
        {
            if (!AppliesTo(rule, agentName))
            {
                continue;
            }

            var match = TriggerMatcher.FindFirstMatch(requestBody, rule.Triggers);
            if (match is not null)
            {
                return new BlockedRequestMatch(rule.DetectorId, rule.DetectorName, match.Trigger.Pattern);
            }
        }

        return null;
    }

    private static bool AppliesTo(BlockingDetectorRule rule, string? agentName)
        => rule.AllAgents
           || (agentName is { Length: > 0 }
               && rule.ScopedAgentNames.Contains(agentName, StringComparer.OrdinalIgnoreCase));
}
