using AwesomeAssertions;
using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class ProviderEndpointsTests
{
    [TestMethod]
    [DataRow("https://my-resource.openai.azure.com/", true)]
    [DataRow("https://eastus.api.cognitive.microsoft.azure.com/", true)]
    [DataRow("https://azure.com/v1", true)]
    [DataRow("https://api.openai.com/v1", false)]
    [DataRow("https://api.anthropic.com/v1", false)]
    // Lookalike hosts must not match: the suffix is a domain boundary, not a substring.
    [DataRow("https://my-azure.com.example.net/v1", false)]
    [DataRow("https://notazure.com/v1", false)]
    public void IsAzure_DetectsByHost(string endpoint, bool expected)
    {
        ProviderEndpoints.IsAzure(new Uri(endpoint)).Should().Be(expected);
    }

    [TestMethod]
    // Uri.Host preserves the DNS root dot, so a fully-qualified hostname must still classify.
    [DataRow("https://resource.openai.azure.com./", true)]
    [DataRow("https://azure.com./v1", true)]
    // …without weakening the domain-boundary check for lookalikes.
    [DataRow("https://my-azure.com.example.net./v1", false)]
    [DataRow("https://notazure.com./v1", false)]
    public void IsAzure_WithTrailingRootDot_DetectsByHost(string endpoint, bool expected)
    {
        ProviderEndpoints.IsAzure(new Uri(endpoint)).Should().Be(expected);
    }
}
