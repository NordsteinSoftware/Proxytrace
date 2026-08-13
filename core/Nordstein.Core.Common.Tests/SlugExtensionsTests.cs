using AwesomeAssertions;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class SlugExtensionsTests
{
    [TestMethod]
    [DataRow("Showcase Project", "showcase-project")]
    [DataRow("OpenAI (demo)", "openai-demo")]
    [DataRow("  leading and trailing  ", "leading-and-trailing")]
    [DataRow("Multiple   spaces", "multiple-spaces")]
    [DataRow("already-slugged", "already-slugged")]
    [DataRow("Mixed_Case_Underscores", "mixed-case-underscores")]
    [DataRow("ünïcode 123", "ünïcode-123")]
    public void ToSlug_DerivesExpectedSlug(string input, string expected)
        => input.ToSlug().Should().Be(expected);
}
