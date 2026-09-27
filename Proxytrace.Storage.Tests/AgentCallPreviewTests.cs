using AwesomeAssertions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Storage.Internal.Entities.AgentCall;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Direct unit tests for the shared preview builder used at ingestion AND by the backfill. The
/// backfill/list tests only assert the preview is non-null; these pin the actual text rules
/// (latest user message, whitespace collapse, truncation) so a silent change to them is caught.
/// </summary>
[TestClass]
public sealed class AgentCallPreviewTests
{
    [TestMethod]
    public void Build_UserMessage_CollapsesWhitespace()
    {
        var request = new Conversation([
            Message.CreateSystemMessage("you are helpful"),
            Message.CreateUserMessage("  Where   is\tmy\norder?  "),
        ]);

        AgentCallPreview.Build(request).Should().Be("Where is my order?");
    }

    [TestMethod]
    public void Build_UsesTheLatestUserMessage()
    {
        var request = new Conversation([
            Message.CreateUserMessage("first question"),
            Message.CreateAssistantMessage([Content.FromText("first answer")], []),
            Message.CreateUserMessage("  second  question  "),
            Message.CreateAssistantMessage([Content.FromText("tool request")], []),
        ]);

        AgentCallPreview.Build(request).Should().Be("second question");
    }

    [TestMethod]
    public void Build_NoUserMessage_ReturnsNull()
    {
        var request = new Conversation([Message.CreateSystemMessage("system only")]);

        AgentCallPreview.Build(request).Should().BeNull();
    }

    [TestMethod]
    public void Build_BlankUserMessage_ReturnsNull()
    {
        var request = new Conversation([Message.CreateUserMessage("   \n\t  ")]);

        AgentCallPreview.Build(request).Should().BeNull();
    }

    [TestMethod]
    public void Build_OverlongText_TruncatesToMaxLength()
    {
        var request = new Conversation([Message.CreateUserMessage(new string('a', AgentCallPreview.MaxLength + 50))]);

        var preview = AgentCallPreview.Build(request);

        preview.Should().NotBeNull();
        preview.Should().HaveLength(AgentCallPreview.MaxLength);
    }
}
