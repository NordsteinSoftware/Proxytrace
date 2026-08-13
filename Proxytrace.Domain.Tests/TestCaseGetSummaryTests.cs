using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.TestCase;
using Nordstein.Core.Testing;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class TestCaseGetSummaryTests : BaseTest<Module>
{
    [TestMethod]
    public void GetSummary_NoUserMessage_ReturnsFallback()
    {
        var testCase = BuildTestCase(new SystemMessage("only system"));

        testCase.GetSummary().Should().Be("Test case");
    }

    [TestMethod]
    public void GetSummary_ShortUserMessage_ReturnsFullText()
    {
        var testCase = BuildTestCase(new UserMessage([Content.FromText("How are you?")]));

        testCase.GetSummary().Should().Be("How are you?");
    }

    [TestMethod]
    public void GetSummary_LongUserMessage_TruncatesAtMaxLengthAndAppendsEllipsis()
    {
        var text = new string('x', 200);
        var testCase = BuildTestCase(new UserMessage([Content.FromText(text)]));

        testCase.GetSummary().Should().Be(new string('x', 77) + "…");
    }

    [TestMethod]
    public void GetSummary_TextAtBoundary_DoesNotTruncate()
    {
        var text = new string('x', 80);
        var testCase = BuildTestCase(new UserMessage([Content.FromText(text)]));

        testCase.GetSummary().Should().Be(text);
    }

    [TestMethod]
    public void GetSummary_PicksFirstUserMessage()
    {
        var testCase = BuildTestCase(
            new UserMessage([Content.FromText("first")]),
            new UserMessage([Content.FromText("second")]));

        testCase.GetSummary().Should().Be("first");
    }

    [TestMethod]
    public void GetSummary_CustomMaxLength_IsRespected()
    {
        var text = new string('y', 50);
        var testCase = BuildTestCase(new UserMessage([Content.FromText(text)]));

        testCase.GetSummary(maxLength: 10).Should().Be(new string('y', 10) + "…");
    }

    private ITestCase BuildTestCase(params Message.Message[] messages)
    {
        var services = GetServices();
        var factory = services.GetRequiredService<ITestCase.CreateNew>();
        var input = new Conversation(messages);
        var expectedOutput = new AssistantMessage([Content.FromText("ok")], []);
        return factory(input, expectedOutput, sourceAgentCallId: null);
    }
}
