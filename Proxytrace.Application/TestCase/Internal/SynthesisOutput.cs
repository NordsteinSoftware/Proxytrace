using System.ComponentModel;
using JetBrains.Annotations;

namespace Proxytrace.Application.TestCase.Internal;

/// <summary>
/// The structured output the synthesis agent returns. Every <see cref="DescriptionAttribute"/> is
/// lifted into the JSON schema the model is prompted with, so these strings are the only per-field
/// guidance it gets. Ids arrive as strings and tool arguments as JSON-encoded strings deliberately:
/// a nested free-form object degrades the schema badly, and everything is re-validated server-side.
/// Load-bearing fields come first, because truncation repair salvages the prefix.
/// </summary>
[UsedImplicitly]
internal sealed record SynthesisOutput
{
    /// <summary>
    /// Gets or sets the summary.
    /// </summary>
    [Description("one sentence describing what this conversation does")]
    public required string Summary { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the proposals.
    /// </summary>
    [Description("the test cases worth building, most consequential first; at most 10")]
    public required IReadOnlyList<SynthesisProposal> Proposals { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the skipped.
    /// </summary>
    [Description("assistant turns you deliberately did not propose, each with the reason why")]
    public IReadOnlyList<SynthesisSkipped>? Skipped { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the evaluator suggestion.
    /// </summary>
    [Description("an agentic judge to add when the destination suite cannot score these cases; omit otherwise")]
    public SynthesisEvaluatorSuggestion? EvaluatorSuggestion { get; [UsedImplicitly] init; }
}

[UsedImplicitly]
internal sealed record SynthesisProposal
{
    /// <summary>
    /// Gets or sets the agent call id.
    /// </summary>
    [Description("the agentCallId of the call this case is built from, copied exactly from the transcript")]
    public required string AgentCallId { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the kind.
    /// </summary>
    [Description("Promotion to lock in what the agent did, Correction to assert what it should have done")]
    public required ProposalKind Kind { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the title.
    /// </summary>
    [Description("a short label for the case, e.g. 'Looks up the order before refunding'")]
    public required string Title { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    [Description("one or two sentences: why this turn is worth testing, and for a Correction what is wrong")]
    public required string Rationale { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the relevance.
    /// </summary>
    [Description("High, Medium or Low — how consequential the decision at this turn is")]
    public required ProposalRelevance Relevance { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the expected content.
    /// </summary>
    [Description("for a Correction only: the assistant text the agent should have produced; empty when it should call tools instead")]
    public string? ExpectedContent { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the expected tool requests.
    /// </summary>
    [Description("for a Correction only: the tool calls the agent should have made")]
    public IReadOnlyList<SynthesisToolRequest>? ExpectedToolRequests { get; [UsedImplicitly] init; }
}

[UsedImplicitly]
internal sealed record SynthesisToolRequest
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    [Description("the tool name; must be one of the tools the agent was offered")]
    public required string Name { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the arguments.
    /// </summary>
    [Description("the arguments as a JSON-encoded object string, e.g. {\"order_id\":\"91\"}")]
    public required string Arguments { get; [UsedImplicitly] init; }
}

[UsedImplicitly]
internal sealed record SynthesisSkipped
{
    /// <summary>
    /// Gets or sets the agent call id.
    /// </summary>
    [Description("the agentCallId of the skipped turn")]
    public required string AgentCallId { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the reason.
    /// </summary>
    [Description("why it is not worth a test case, e.g. 'closing summary — grades prose only'")]
    public required string Reason { get; [UsedImplicitly] init; }
}

[UsedImplicitly]
internal sealed record SynthesisEvaluatorSuggestion
{
    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    [Description("a short name for the judge, e.g. 'Refund policy judge'")]
    public required string Name { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the instructions.
    /// </summary>
    [Description("the judge's system prompt: what it should check and how it should decide")]
    public required string Instructions { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the reason.
    /// </summary>
    [Description("why the destination suite's current evaluators cannot score these cases")]
    public required string Reason { get; [UsedImplicitly] init; }

    /// <summary>
    /// Gets or sets the target.
    /// </summary>
    [Description("Attach to add the judge to the destination suite (it will also score its other cases), or NewSuite to put these cases in a fresh suite instead")]
    public required EvaluatorSuggestionTarget Target { get; [UsedImplicitly] init; }
}
