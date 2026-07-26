using AwesomeAssertions;
using Proxytrace.Domain.Message;

namespace Proxytrace.Domain.Tests;

/// <summary>
/// <see cref="Conversation.ResolvedToolCallCount"/> is what tells a decision point apart from a
/// summary: a conversation captured at the last call of an agent's tool loop already holds every
/// tool call the agent made and every result it got, so a test case built on it can only grade the
/// closing message. These tests pin the counting rule that distinction rests on.
/// </summary>
[TestClass]
public sealed class ConversationResolvedToolCallCountTests
{
    [TestMethod]
    public void ResolvedToolCallCount_PlainConversation_IsZero()
    {
        var conversation = new Conversation([User("Can I still return this?")]);

        conversation.ResolvedToolCallCount.Should().Be(0);
    }

    [TestMethod]
    public void ResolvedToolCallCount_UnansweredToolCall_IsZero()
    {
        // The agent asked for a tool but the result has not come back — the next model call is
        // still the tool call itself, not a summary of it.
        var conversation = new Conversation(
        [
            User("Can I still return order 20114?"),
            AssistantCalling("call-1", "lookup_order")
        ]);

        conversation.ResolvedToolCallCount.Should().Be(0);
    }

    [TestMethod]
    public void ResolvedToolCallCount_DecisionPoint_CountsOnlyTheAnsweredCalls()
    {
        // The shape of a usable regression case: the lookup came back, and the very next thing the
        // model produces is the decision. One call resolved, the decision still open.
        var conversation = new Conversation(
        [
            User("Can I still return order 20114?"),
            AssistantCalling("call-1", "lookup_order"),
            ToolResult("call-1", """{"delivered_days_ago":45}""")
        ]);

        conversation.ResolvedToolCallCount.Should().Be(1);
    }

    [TestMethod]
    public void ResolvedToolCallCount_CompletedToolLoop_CountsEveryAnsweredCall()
    {
        // The shape that cannot be corrected: every call the agent made, including the harmful
        // one, already succeeded in the input.
        var conversation = new Conversation(
        [
            User("Maria promised me a refund."),
            AssistantCalling("call-1", "lookup_order"),
            ToolResult("call-1", """{"delivered_days_ago":45}"""),
            AssistantCalling("call-2", "start_return"),
            ToolResult("call-2", """{"error":"return_not_available"}"""),
            AssistantCalling("call-3", "issue_refund"),
            ToolResult("call-3", """{"refund_id":"REF-21108","percent":100}""")
        ]);

        conversation.ResolvedToolCallCount.Should().Be(3);
    }

    [TestMethod]
    public void ResolvedToolCallCount_ToolResultForAnotherCall_IsNotCounted()
    {
        // Pairing is by tool-call id, not by position — an orphaned result must not make an
        // unanswered call look answered.
        var conversation = new Conversation(
        [
            User("Can I still return order 20114?"),
            AssistantCalling("call-1", "lookup_order"),
            ToolResult("some-other-call", """{"delivered_days_ago":45}""")
        ]);

        conversation.ResolvedToolCallCount.Should().Be(0);
    }

    [TestMethod]
    public void ResolvedToolCallCount_MalformedToolMessage_DoesNotThrow()
    {
        // A ToolMessage missing its result slot is invalid, but reading a descriptive count must
        // not be the thing that fails — ToolMessage.Id would throw here.
        var conversation = new Conversation(
        [
            User("Can I still return order 20114?"),
            AssistantCalling("call-1", "lookup_order"),
            new ToolMessage([Content.FromText("call-1")])
        ]);

        conversation.ResolvedToolCallCount.Should().Be(1);
    }

    [TestMethod]
    public void ResolvedToolCallCount_EmptyToolMessage_DoesNotThrow()
    {
        var conversation = new Conversation(
        [
            User("Can I still return order 20114?"),
            AssistantCalling("call-1", "lookup_order"),
            new ToolMessage([])
        ]);

        conversation.ResolvedToolCallCount.Should().Be(0);
    }

    private static UserMessage User(string text)
        => new([Content.FromText(text)]);

    private static AssistantMessage AssistantCalling(string id, string toolName)
        => new([], [new ToolRequest(id, toolName, "{}")]);

    private static ToolMessage ToolResult(string id, string payload)
        => new(new ToolResponse(id, [Content.FromText(payload)], success: true, error: null));
}
