namespace Proxytrace.Application.TestCase;

/// <summary>Whether a proposal locks in the recorded behaviour or asserts a corrected one.</summary>
public enum ProposalKind
{
    /// <summary>GREEN — expected output is the response the agent actually gave.</summary>
    Promotion = 0,

    /// <summary>RED — expected output is the answer the agent should have given; fails until fixed.</summary>
    Correction = 1,
}

/// <summary>How consequential the proposed case is; drives sort order and pre-selection.</summary>
public enum ProposalRelevance
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>A problem the server found with a proposal. Flagged proposals are never pre-selected.</summary>
public enum ProposalFlag
{
    /// <summary>
    /// A correction on a call whose input already contains resolved tool calls. The harmful call
    /// already succeeded in the input, so no expected output can contradict it — the case would fail
    /// forever while reading as "the fix did not work". See <c>docs/optimization-loop.md</c>.
    /// </summary>
    Unpassable = 0,

    /// <summary>The expected output calls a tool the agent was not offered on that call.</summary>
    UnknownTool = 1,
}

public sealed record ProposedToolRequest(string Name, string Arguments);

public sealed record ProposedExpectedOutput(string Content, IReadOnlyList<ProposedToolRequest> ToolRequests);

public sealed record TestCaseProposal
{
    /// <summary>The call of the conversation the case is built from; its request becomes the input.</summary>
    public required Guid AgentCallId { get; init; }

    public required ProposalKind Kind { get; init; }

    /// <summary>Short label, e.g. "Looks up the order before refunding".</summary>
    public required string Title { get; init; }

    /// <summary>One or two sentences: why this turn is worth testing.</summary>
    public required string Rationale { get; init; }

    public required ProposalRelevance Relevance { get; init; }

    /// <summary>Set for a <see cref="ProposalKind.Correction"/> only; null means promote as-is.</summary>
    public ProposedExpectedOutput? ExpectedOutput { get; init; }

    public IReadOnlyList<ProposalFlag> Flags { get; init; } = [];
}

/// <summary>A turn the agent deliberately did not propose, with its reason — so the judgement is auditable.</summary>
public sealed record SkippedTurn(Guid AgentCallId, string Reason);

/// <summary>Where an evaluator suggestion should land.</summary>
public enum EvaluatorSuggestionTarget
{
    /// <summary>Attach the judge to the destination suite — it will also score its other cases.</summary>
    Attach = 0,

    /// <summary>Put the approved cases in a new suite carrying the judge instead.</summary>
    NewSuite = 1,
}

public sealed record EvaluatorSuggestion
{
    public required string Name { get; init; }

    /// <summary>The judge's system prompt / rubric.</summary>
    public required string Instructions { get; init; }

    /// <summary>Why the destination suite's current evaluators cannot score these proposals.</summary>
    public required string Reason { get; init; }

    public required EvaluatorSuggestionTarget Target { get; init; }
}

public sealed record TestCaseProposalSet
{
    /// <summary>Hard cap on proposals per round.</summary>
    public const int MaxProposals = 10;

    /// <summary>Hard cap on refinement rounds carried into one request.</summary>
    public const int MaxRounds = 5;

    public required string Summary { get; init; }

    public required IReadOnlyList<TestCaseProposal> Proposals { get; init; }

    public required IReadOnlyList<SkippedTurn> Skipped { get; init; }

    public EvaluatorSuggestion? EvaluatorSuggestion { get; init; }

    public static TestCaseProposalSet Empty { get; } = new()
    {
        Summary = string.Empty,
        Proposals = [],
        Skipped = [],
    };
}

/// <summary>One completed refinement exchange: the instruction that produced these proposals.</summary>
public sealed record SynthesisRound(string? Instruction, TestCaseProposalSet Proposals);
