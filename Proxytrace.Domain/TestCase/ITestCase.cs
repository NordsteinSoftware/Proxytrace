using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Messages;

namespace Proxytrace.Domain.TestCase;

/// <summary>
/// Represents a single test case consisting of an input conversation and the expected assistant response.
/// </summary>
public interface ITestCase : IDomainEntity
{
    /// <summary>The conversation sent to the agent as input.</summary>
    Conversation Input { get; }

    /// <summary>The expected assistant response used for evaluation.</summary>
    AssistantMessage ExpectedOutput { get; }

    /// <summary>
    /// The <see cref="IAgentCall"/> this case was promoted or corrected from, if any. Provenance link
    /// back to the source trace, so the chain <c>trace → case</c> stays answerable. A denormalized
    /// snapshot (a plain id, not a foreign key): synthetic cases built from raw input/expected output
    /// carry <see langword="null"/>, and the case deliberately survives deletion of its source trace.
    /// </summary>
    Guid? SourceAgentCallId { get; }

    /// <summary>
    /// Returns a short, single-line label for this test case based on its first user message,
    /// truncated to <paramref name="maxLength"/> characters with an ellipsis when the original
    /// text exceeds <paramref name="maxLength"/> + 3 characters.
    /// Returns <c>"Test case"</c> when no user message is present.
    /// </summary>
    string GetSummary(int maxLength = 77);

    /// <summary>
    /// Factory delegate for promoting a trace as-is: the expected output is the response the agent
    /// actually recorded, and <see cref="SourceAgentCallId"/> is set to the call's id.
    /// </summary>
    public delegate ITestCase CreateNewFromCall(IAgentCall agentCall);

    /// <summary>
    /// Factory delegate for recording a human correction against a trace: the input is the call's
    /// request, the expected output is the corrected answer, and <see cref="SourceAgentCallId"/> is
    /// set to the call's id.
    /// </summary>
    public delegate ITestCase CreateCorrection(IAgentCall agentCall, AssistantMessage expectedOutput);

    /// <summary>
    /// Factory delegate for creating a test case directly from an input conversation and expected
    /// output. Pass <paramref name="sourceAgentCallId"/> = <see langword="null"/> for a synthetic case,
    /// or the id of the trace it derives from.
    /// </summary>
    public delegate ITestCase CreateNew(Conversation input, AssistantMessage expectedOutput, Guid? sourceAgentCallId);

    /// <summary>Factory delegate for reconstituting an existing test case from persistence.</summary>
    public delegate ITestCase CreateExisting(
        Conversation input,
        AssistantMessage expectedOutput,
        Guid? sourceAgentCallId,
        IDomainEntityData existing);
}
