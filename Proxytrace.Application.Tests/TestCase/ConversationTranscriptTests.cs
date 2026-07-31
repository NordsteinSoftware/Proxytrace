using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Application.TestCase.Internal;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Application.Tests.TestCase;

[TestClass]
public sealed class ConversationTranscriptTests
{
    [TestMethod]
    public void Build_LabelsEveryCallWithItsId()
    {
        var first = Call(
            Conversation.Create().With(Message.CreateUserMessage("refund pls")),
            new AssistantMessage([], [new ToolRequest("t1", "get_order", """{"id":"91"}""")]));

        var transcript = ConversationTranscript.Build([first]);

        transcript.Text.Should().Contain($"agentCallId={first.Id}");
        transcript.Text.Should().Contain("get_order");
        transcript.Clipped.Should().BeFalse();
    }

    [TestMethod]
    public void Build_EmitsEachTurnOnce_NotTheRepeatedHistory()
    {
        // Every captured call re-contains the whole prior conversation. Emitting all of them
        // verbatim would repeat the first user turn once per call and blow the budget.
        var opening = Conversation.Create().With(Message.CreateUserMessage("refund pls"));
        var checking = new AssistantMessage([Content.FromText("checking")], []);
        var first = Call(opening, checking);
        var second = Call(
            opening.With(checking).With(Message.CreateUserMessage("any news?")),
            new AssistantMessage([Content.FromText("done")], []));

        var transcript = ConversationTranscript.Build([first, second]);

        transcript.Text.Split("refund pls").Length.Should().Be(2, "the opening turn appears exactly once");
        transcript.Text.Should().Contain("any news?");
    }

    [TestMethod]
    public void Build_ReportsResolvedToolCallCountPerCall()
    {
        var request = new ToolRequest("t1", "get_order", "{}");
        var conversation = Conversation.Create()
            .With(Message.CreateUserMessage("refund pls"))
            .With(new AssistantMessage([], [request]))
            .With(Message.CreateToolMessage(new ToolResponse(request, [Content.FromText("ok")])));
        var call = Call(conversation, new AssistantMessage([Content.FromText("done")], []));

        var transcript = ConversationTranscript.Build([call]);

        transcript.Text.Should().Contain("resolvedToolCalls=1");
    }

    [TestMethod]
    public void Build_IncludesTheAgentsSystemPromptAndToolSchema()
    {
        var call = Call(
            Conversation.Create().With(Message.CreateUserMessage("hi")),
            new AssistantMessage([Content.FromText("hello")], []));

        var transcript = ConversationTranscript.Build([call]);

        transcript.Text.Should().Contain("You are a support agent.");
        transcript.Text.Should().Contain("get_order");
    }

    [TestMethod]
    public void Build_ClipsAnOversizedMessageAndFlagsIt()
    {
        string huge = new('x', ConversationTranscript.MessageCharMax + 5_000);
        var call = Call(
            Conversation.Create().With(Message.CreateUserMessage(huge)),
            new AssistantMessage([Content.FromText("ok")], []));

        var transcript = ConversationTranscript.Build([call]);

        transcript.Clipped.Should().BeTrue();
        transcript.Text.Length.Should().BeLessThan(huge.Length);
    }

    [TestMethod]
    public void Build_WithNoCalls_IsEmpty()
    {
        var transcript = ConversationTranscript.Build([]);

        transcript.Text.Should().BeEmpty();
        transcript.Clipped.Should().BeFalse();
    }

    [TestMethod]
    public void FairShareCap_WhenEverythingFits_IsUnbounded()
    {
        ConversationTranscript.FairShareCap([10, 20, 30], budget: 1000).Should().Be(int.MaxValue);
    }

    [TestMethod]
    public void FairShareCap_ClipsOnlyTheOutsizedEntry()
    {
        // 10 + 10 + 980 against a 200 budget: the two short ones survive intact.
        int cap = ConversationTranscript.FairShareCap([10, 10, 980], budget: 200);

        cap.Should().BeGreaterThan(10);
        cap.Should().BeLessThan(980);
    }

    private static IAgentCall Call(Conversation request, AssistantMessage response)
    {
        var prompt = Substitute.For<IPromptTemplate>();
        prompt.Template.Returns("You are a support agent.");

        var version = Substitute.For<IAgentVersion>();
        version.SystemPrompt.Returns(prompt);
        version.Tools.Returns(new List<ToolSpecification>
        {
            new("get_order", "Look up an order.", ToolArguments.None),
        });

        var agent = Substitute.For<IAgent>();
        agent.Name.Returns("Support bot");

        var completion = Substitute.For<ICompletion>();
        completion.Response.Returns(response);

        var call = Substitute.For<IAgentCall>();
        call.Id.Returns(Guid.NewGuid());
        call.Agent.Returns(agent);
        call.Version.Returns(version);
        call.Request.Returns(request);
        call.Response.Returns(completion);
        return call;
    }
}
