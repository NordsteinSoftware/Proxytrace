using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Application.TestCase.Internal;

/// <summary>
/// Turns the model's raw output into a validated proposal set.
///
/// NOTHING the model says about ids, tool names, or turn choice is trusted — every claim is
/// re-checked against the real conversation, so a hallucinated agentCallId can never become a test
/// case. Problems that are worth showing rather than hiding (an unpassable correction, an unknown
/// tool) become flags: the proposal survives, carries the warning, and is never pre-selected.
/// </summary>
internal static class ProposalValidator
{
    public static TestCaseProposalSet Validate(
        SynthesisOutput output,
        IReadOnlyList<IAgentCall> conversation)
    {
        Dictionary<Guid, IAgentCall> byId = conversation
            .GroupBy(call => call.Id)
            .ToDictionary(group => group.Key, group => group.First());

        HashSet<(Guid Call, ProposalKind Kind)> seen = [];
        List<TestCaseProposal> accepted = [];

        foreach (SynthesisProposal raw in output.Proposals)
        {
            if (!Guid.TryParse(raw.AgentCallId, out Guid callId)
                || !byId.TryGetValue(callId, out IAgentCall? call))
            {
                continue;
            }

            // CreateNewFromCall throws without a response, and there is nothing to promote anyway.
            if (call.Response is null)
            {
                continue;
            }

            ProposedExpectedOutput? expected = MapExpected(raw);

            // A correction with no expected answer is not a correction.
            if (raw.Kind == ProposalKind.Correction && expected is null)
            {
                continue;
            }

            if (!seen.Add((callId, raw.Kind)))
            {
                continue;
            }

            accepted.Add(new TestCaseProposal
            {
                AgentCallId = callId,
                Kind = raw.Kind,
                Title = raw.Title,
                Rationale = raw.Rationale,
                Relevance = raw.Relevance,
                ExpectedOutput = raw.Kind == ProposalKind.Correction ? expected : null,
                Flags = Flags(raw.Kind, raw.Kind == ProposalKind.Correction ? expected : null, call),
            });
        }

        return new TestCaseProposalSet
        {
            Summary = output.Summary,
            Proposals =
            [
                .. accepted
                    .OrderByDescending(proposal => proposal.Relevance)
                    .Take(TestCaseProposalSet.MaxProposals),
            ],
            Skipped =
            [
                .. (output.Skipped ?? [])
                    .Select(skipped => (
                        Parsed: Guid.TryParse(skipped.AgentCallId, out Guid id),
                        Id: id,
                        skipped.Reason))
                    .Where(entry => entry.Parsed && byId.ContainsKey(entry.Id))
                    .Select(entry => new SkippedTurn(entry.Id, entry.Reason)),
            ],
            EvaluatorSuggestion = MapSuggestion(output.EvaluatorSuggestion),
        };
    }

    private static IReadOnlyList<ProposalFlag> Flags(
        ProposalKind kind,
        ProposedExpectedOutput? expected,
        IAgentCall call)
    {
        List<ProposalFlag> flags = [];

        // The unpassable-correction trap: the input already contains the tool calls the agent made
        // AND their results, so the only producible output is the closing summary and no expected
        // output can contradict what already happened. See docs/optimization-loop.md.
        if (kind == ProposalKind.Correction && call.Request.ResolvedToolCallCount > 0)
        {
            flags.Add(ProposalFlag.Unpassable);
        }

        if (expected is not null)
        {
            HashSet<string> offered = [.. call.Version.Tools.Select(tool => tool.Name)];
            if (expected.ToolRequests.Any(request => !offered.Contains(request.Name)))
            {
                flags.Add(ProposalFlag.UnknownTool);
            }
        }

        return flags;
    }

    private static ProposedExpectedOutput? MapExpected(SynthesisProposal raw)
    {
        IReadOnlyList<ProposedToolRequest> toolRequests =
        [
            .. (raw.ExpectedToolRequests ?? [])
                .Where(request => !string.IsNullOrWhiteSpace(request.Name))
                .Select(request => new ProposedToolRequest(
                    request.Name.Trim(),
                    string.IsNullOrWhiteSpace(request.Arguments) ? "{}" : request.Arguments)),
        ];

        string content = raw.ExpectedContent?.Trim() ?? string.Empty;
        return content.Length == 0 && toolRequests.Count == 0
            ? null
            : new ProposedExpectedOutput(content, toolRequests);
    }

    private static EvaluatorSuggestion? MapSuggestion(SynthesisEvaluatorSuggestion? raw)
        => raw is null || string.IsNullOrWhiteSpace(raw.Name) || string.IsNullOrWhiteSpace(raw.Instructions)
            ? null
            : new EvaluatorSuggestion
            {
                Name = raw.Name.Trim(),
                Instructions = raw.Instructions,
                Reason = raw.Reason,
                Target = raw.Target,
            };
}
