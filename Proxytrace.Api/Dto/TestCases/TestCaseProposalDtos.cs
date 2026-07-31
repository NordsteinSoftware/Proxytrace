using System.ComponentModel.DataAnnotations;
using Proxytrace.Application.TestCase;

namespace Proxytrace.Api.Dto.TestCases;

public record SynthesizeTestCasesRequest(
    Guid? SuiteId = null,
    [StringLength(2000)] string? Instruction = null,
    [MaxLength(TestCaseProposalSet.MaxRounds)] IReadOnlyList<SynthesisRoundDto>? Rounds = null);

public record SynthesisRoundDto(
    [StringLength(2000)] string? Instruction,
    TestCaseProposalSetDto Proposals);

public record ProposedToolRequestDto(string Name, string Arguments);

public record ProposedExpectedOutputDto(string Content, IReadOnlyList<ProposedToolRequestDto> ToolRequests);

public record TestCaseProposalDto(
    Guid AgentCallId,
    ProposalKind Kind,
    string Title,
    string Rationale,
    ProposalRelevance Relevance,
    ProposedExpectedOutputDto? ExpectedOutput,
    IReadOnlyList<ProposalFlag> Flags);

public record SkippedTurnDto(Guid AgentCallId, string Reason);

public record EvaluatorSuggestionDto(
    string Name,
    string Instructions,
    string Reason,
    EvaluatorSuggestionTarget Target);

public record TestCaseProposalSetDto(
    string Summary,
    IReadOnlyList<TestCaseProposalDto> Proposals,
    IReadOnlyList<SkippedTurnDto> Skipped,
    EvaluatorSuggestionDto? EvaluatorSuggestion);

/// <summary>
/// Maps between the Application proposal contracts and their wire DTOs, both ways — a refinement
/// round posts back proposals the client may have edited.
/// </summary>
public sealed class TestCaseProposalDtoMapper
{
    public TestCaseProposalSetDto ToDto(TestCaseProposalSet set)
        => new(
            set.Summary,
            [.. set.Proposals.Select(ToDto)],
            [.. set.Skipped.Select(skipped => new SkippedTurnDto(skipped.AgentCallId, skipped.Reason))],
            set.EvaluatorSuggestion is { } suggestion
                ? new EvaluatorSuggestionDto(
                    suggestion.Name, suggestion.Instructions, suggestion.Reason, suggestion.Target)
                : null);

    public SynthesisRound ToDomain(SynthesisRoundDto dto)
        => new(dto.Instruction, ToDomain(dto.Proposals));

    private static TestCaseProposalDto ToDto(TestCaseProposal proposal)
        => new(
            proposal.AgentCallId,
            proposal.Kind,
            proposal.Title,
            proposal.Rationale,
            proposal.Relevance,
            proposal.ExpectedOutput is { } expected
                ? new ProposedExpectedOutputDto(
                    expected.Content,
                    [.. expected.ToolRequests.Select(request =>
                        new ProposedToolRequestDto(request.Name, request.Arguments))])
                : null,
            proposal.Flags);

    private static TestCaseProposalSet ToDomain(TestCaseProposalSetDto dto)
        => new()
        {
            Summary = dto.Summary,
            Proposals =
            [
                .. dto.Proposals.Select(proposal => new TestCaseProposal
                {
                    AgentCallId = proposal.AgentCallId,
                    Kind = proposal.Kind,
                    Title = proposal.Title,
                    Rationale = proposal.Rationale,
                    Relevance = proposal.Relevance,
                    ExpectedOutput = proposal.ExpectedOutput is { } expected
                        ? new ProposedExpectedOutput(
                            expected.Content,
                            [.. expected.ToolRequests.Select(request =>
                                new ProposedToolRequest(request.Name, request.Arguments))])
                        : null,
                    Flags = proposal.Flags,
                }),
            ],
            Skipped = [.. dto.Skipped.Select(skipped => new SkippedTurn(skipped.AgentCallId, skipped.Reason))],
        };
}
