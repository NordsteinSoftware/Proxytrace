using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Application.TestCase.Internal;

/// <summary>
/// Turns the model's raw output into a validated proposal set. NOTHING the model says about ids,
/// tool names, or turn choice is trusted — every claim is re-checked against the real conversation.
/// </summary>
internal static class ProposalValidator
{
    public static TestCaseProposalSet Validate(
        SynthesisOutput output,
        IReadOnlyList<IAgentCall> conversation)
        => new()
        {
            Summary = output.Summary,
            Proposals =
            [
                .. output.Proposals
                    .Where(proposal => Guid.TryParse(proposal.AgentCallId, out _))
                    .Select(proposal => new TestCaseProposal
                    {
                        AgentCallId = Guid.Parse(proposal.AgentCallId),
                        Kind = proposal.Kind,
                        Title = proposal.Title,
                        Rationale = proposal.Rationale,
                        Relevance = proposal.Relevance,
                    }),
            ],
            Skipped = [],
        };
}
