using AwesomeAssertions;
using Nordstein.Core.Common.Net;

namespace Nordstein.Core.Common.Tests;

[TestClass]
public sealed class EndpointUrlExtensionsTests
{
    [TestMethod]
    [DataRow("https://api.openai.com/v1", "https://api.openai.com/v1")]
    [DataRow("http://localhost:8080/v1", "http://localhost:8080/v1")]
    [DataRow("api.openai.com/v1", "https://api.openai.com/v1")]
    [DataRow("openai.com", "https://openai.com/")]
    [DataRow("localhost:5000", "https://localhost:5000/")]
    [DataRow("  api.openai.com/v1  ", "https://api.openai.com/v1")]
    public void ToEndpointUri_ParsesAndDefaultsToHttps(string input, string expected)
        => input.ToEndpointUri().Should().Be(new Uri(expected));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("://missing-scheme")]
    [DataRow("ftp://api.openai.com/v1")]
    [DataRow("https://")]
    public void ToEndpointUri_InvalidInput_Throws(string input)
    {
        var action = () => input.ToEndpointUri();
        action.Should().Throw<MalformedEndpointUrlException>();
    }
}
