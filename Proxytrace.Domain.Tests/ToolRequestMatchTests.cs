using AwesomeAssertions;
using Proxytrace.Domain.Message;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ToolRequestMatchTests
{
    [TestMethod]
    public void Matches_IgnoresTheToolCallId()
    {
        // Expected-output ids are minted locally (TestSuiteDtoMapper uses Guid.NewGuid());
        // the actual id comes from the provider. Comparing them could never match.
        var expected = new ToolRequest("local-1", "get_order", """{"order_id":"91"}""");
        var actual = new ToolRequest("call_abc123", "get_order", """{"order_id":"91"}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_IgnoresKeyOrderAndWhitespace()
    {
        var expected = new ToolRequest("a", "issue_refund", """{"order_id":"91","amount":40}""");
        var actual = new ToolRequest("b", "issue_refund", "{\n  \"amount\": 40,\n  \"order_id\": \"91\"\n}");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_ComparesNumbersByValue()
    {
        var expected = new ToolRequest("a", "issue_refund", """{"amount":40}""");
        var actual = new ToolRequest("b", "issue_refund", """{"amount":40.0}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_WithUnparseableArguments_FallsBackToTrimmedStringEquality()
    {
        var expected = new ToolRequest("a", "note", "  not json  ");
        var actual = new ToolRequest("b", "note", "not json");

        ToolRequestMatch.Matches(expected, actual).Should().BeTrue();
    }

    [TestMethod]
    public void Matches_WithDifferentArgumentValue_IsFalse()
    {
        var expected = new ToolRequest("a", "issue_refund", """{"order_id":"91"}""");
        var actual = new ToolRequest("b", "issue_refund", """{"order_id":"92"}""");

        ToolRequestMatch.Matches(expected, actual).Should().BeFalse();
    }

    [TestMethod]
    public void Matches_WithDifferentToolName_IsFalse()
    {
        var expected = new ToolRequest("a", "get_order", "{}");
        var actual = new ToolRequest("b", "delete_order", "{}");

        ToolRequestMatch.Matches(expected, actual).Should().BeFalse();
    }

    [TestMethod]
    public void Differences_WithSameCallsInAnotherOrder_IsEmpty()
    {
        // Parallel tool calls carry no meaningful order.
        ToolRequest[] expected =
        [
            new("a", "get_order", """{"id":1}"""),
            new("b", "get_customer", """{"id":7}"""),
        ];
        ToolRequest[] actual =
        [
            new("x", "get_customer", """{"id":7}"""),
            new("y", "get_order", """{"id":1}"""),
        ];

        ToolRequestMatch.Differences(expected, actual).Should().BeEmpty();
    }

    [TestMethod]
    public void Differences_WithWrongArgument_NamesBothSides()
    {
        ToolRequest[] expected = [new("a", "issue_refund", """{"order_id":"91"}""")];
        ToolRequest[] actual = [new("b", "issue_refund", """{"order_id":"92"}""")];

        ToolRequestMatch.Differences(expected, actual).Should().ContainSingle()
            .Which.Should().Contain("issue_refund").And.Contain("91").And.Contain("92");
    }

    [TestMethod]
    public void Differences_WithMissingCall_SaysItWasNotCalled()
    {
        ToolRequest[] expected = [new("a", "get_order", "{}")];

        ToolRequestMatch.Differences(expected, []).Should().ContainSingle()
            .Which.Should().Be("Expected tool 'get_order' but it was not called");
    }

    [TestMethod]
    public void Differences_WithExtraCall_ReportsItAsUnexpected()
    {
        ToolRequest[] actual = [new("b", "delete_order", "{}")];

        ToolRequestMatch.Differences([], actual).Should().ContainSingle()
            .Which.Should().Be("Unexpected tool 'delete_order({})'");
    }

    [TestMethod]
    public void Differences_WithNoToolsOnEitherSide_IsEmpty()
    {
        ToolRequestMatch.Differences([], []).Should().BeEmpty();
    }
}
