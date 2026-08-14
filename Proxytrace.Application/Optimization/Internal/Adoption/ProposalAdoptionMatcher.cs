using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.OptimizationProposal;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Optimization.Internal.Adoption;

/// <summary>
/// Decides whether a promoted proposal's change is exactly what an agent now runs. Matching is
/// deliberately exact — a tweaked adoption never auto-matches; the user confirms those manually.
/// </summary>
internal class ProposalAdoptionMatcher
{
    /// <summary>
    /// Whether <paramref name="version"/> carries exactly the change proposed by a
    /// SystemPrompt or Tool proposal. Always false for ModelSwitch proposals (endpoints
    /// live on the agent, not the version — see <see cref="MatchesEndpoint"/>).
    /// </summary>
    public bool MatchesVersion(IOptimizationProposal proposal, IAgentVersion version)
        => proposal switch
        {
            ISystemPromptProposal sp => string.Equals(
                version.SystemPrompt.Template, sp.ProposedSystemMessage, StringComparison.Ordinal),
            IToolUpdateProposal tu => ToolSetEquals(version.Tools, tu.ProposedTools),
            _ => false,
        };

    /// <summary>
    /// Whether the agent now runs on exactly the endpoint a ModelSwitch proposal suggested.
    /// </summary>
    public bool MatchesEndpoint(IOptimizationProposal proposal, IAgent agent)
        => proposal is IModelSwitchProposal ms && agent.Endpoint.Id == ms.ProposedEndpoint.Id;

    /// <summary>
    /// Whether the agent's current state (current version or endpoint, depending on the
    /// proposal kind) already carries the proposed change.
    /// </summary>
    public bool MatchesAgentState(IOptimizationProposal proposal, IAgent agent)
        => proposal is IModelSwitchProposal
            ? MatchesEndpoint(proposal, agent)
            : MatchesVersion(proposal, agent.CurrentVersion);

    /// <summary>
    /// Exact tool-set equality, order-insensitive: same names, descriptions, and argument
    /// schemas. Mirrors the canonicalization of the strict version fingerprint (which cannot be
    /// reused directly because it hashes the system prompt together with the tools).
    /// </summary>
    private static bool ToolSetEquals(
        IReadOnlyList<ToolSpecification> actual,
        IReadOnlyList<ToolSpecification> proposed)
    {
        if (actual.Count != proposed.Count)
            return false;

        var left = actual.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        var right = proposed.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();

        return left.Zip(right).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal)
            && string.Equals(pair.First.Description, pair.Second.Description, StringComparison.Ordinal)
            && string.Equals(pair.First.Arguments.JsonSchema, pair.Second.Arguments.JsonSchema, StringComparison.Ordinal));
    }
}
