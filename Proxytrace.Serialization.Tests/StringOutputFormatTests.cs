using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Proxytrace.Serialization.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Serialization.Tests;

[TestClass]
public sealed class StringOutputFormatTests : BaseTest<Module>
{
    [TestMethod]
    public void ToPromptString_ReturnsNull()
    {
        var format = new StringOutputFormat();
        format.ToPromptString().Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_TargetingString_ReturnsRawInput()
    {
        var format = new StringOutputFormat();
        var result = await format.ParseAsync<string>("hello world", CancellationToken);

        result.Should().Be("hello world");
    }

    [TestMethod]
    public async Task ParseAsync_NullInput_ReturnsNull()
    {
        var format = new StringOutputFormat();
        var result = await format.ParseAsync<string>(null, CancellationToken);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ParseAsync_TargetingNonString_Throws()
    {
        var format = new StringOutputFormat();

        await format.Invoking(f => f.ParseAsync<int>("123", CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [TestMethod]
    public void Validate_ReturnsNoErrors()
    {
        var format = new StringOutputFormat();
        var results = format.Validate(new ValidationContext(format)).ToArray();

        results.Should().BeEmpty();
    }
}
