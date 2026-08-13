using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class LogSafeExtensionsTests
{
    [TestMethod]
    [DataRow("/api/agents", "/api/agents")]
    [DataRow("/x\r\nINFO: admin logged in", "/xINFO: admin logged in")]
    [DataRow("/x\nsecond line", "/xsecond line")]
    [DataRow("/x\rsecond line", "/xsecond line")]
    [DataRow("\r\n\r\n", "")]
    [DataRow("", "")]
    public void ToSingleLogLine_StripsLineBreaks(string input, string expected)
        => input.ToSingleLogLine().Should().Be(expected);

    [TestMethod]
    public void ToSingleLogLine_WithNull_ReturnsEmpty()
        => ((string?)null).ToSingleLogLine().Should().BeEmpty();

    [TestMethod]
    public void ToSingleLogLine_Always_LeavesNoLineBreakBehind()
        => "a\rb\nc\r\nd".ToSingleLogLine().Should().Be("abcd");
}
