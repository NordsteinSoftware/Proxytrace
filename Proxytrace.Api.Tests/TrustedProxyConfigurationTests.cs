using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TrustedProxyConfigurationTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    [TestMethod]
    public void Build_ByDefault_ProcessesForwardedForAndProto()
    {
        var options = new TrustedProxyConfiguration(Configuration()).Build();

        options.Should().NotBeNull();
        options?.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    [TestMethod]
    public void Build_WithNoTrustDeclared_TrustsNothingBeyondTheFrameworkLoopbackDefault()
    {
        // Fail safe: an unrestricted X-Forwarded-For would turn the rate-limit partition key into a
        // client-controlled value, so an unconfigured deployment must trust no extra peer.
        var defaults = new ForwardedHeadersOptions();

        var options = new TrustedProxyConfiguration(Configuration()).Build();

        options.Should().NotBeNull();
        options?.KnownProxies.Should().HaveCount(defaults.KnownProxies.Count);
        options?.KnownIPNetworks.Should().HaveCount(defaults.KnownIPNetworks.Count);
    }

    [TestMethod]
    public void Build_ByDefault_AllowsASingleProxyHop()
    {
        var options = new TrustedProxyConfiguration(Configuration()).Build();

        options?.ForwardLimit.Should().Be(1);
    }

    [TestMethod]
    public void Build_WhenDisabled_ReturnsNoOptions()
    {
        var options = new TrustedProxyConfiguration(Configuration(("ForwardedHeaders:Enabled", "false"))).Build();

        options.Should().BeNull();
    }

    [TestMethod]
    public void Build_WithKnownNetworks_TrustsTheConfiguredRanges()
    {
        var options = new TrustedProxyConfiguration(
            Configuration(("ForwardedHeaders:KnownNetworks", "172.16.0.0/12, 10.0.0.0/8"))).Build();

        options?.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        options?.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("10.0.0.0/8"));
    }

    [TestMethod]
    public void Build_WithKnownNetworksAsAnArray_TrustsTheConfiguredRanges()
    {
        var options = new TrustedProxyConfiguration(
            Configuration(("ForwardedHeaders:KnownNetworks:0", "192.168.0.0/16"))).Build();

        options?.KnownIPNetworks.Should().Contain(System.Net.IPNetwork.Parse("192.168.0.0/16"));
    }

    [TestMethod]
    public void Build_WithKnownProxies_TrustsTheConfiguredAddresses()
    {
        var options = new TrustedProxyConfiguration(
            Configuration(("ForwardedHeaders:KnownProxies", "172.18.0.4"))).Build();

        options?.KnownProxies.Should().Contain(IPAddress.Parse("172.18.0.4"));
    }

    [TestMethod]
    public void Build_WithForwardLimit_UsesTheConfiguredHopCount()
    {
        var options = new TrustedProxyConfiguration(Configuration(("ForwardedHeaders:ForwardLimit", "2"))).Build();

        options?.ForwardLimit.Should().Be(2);
    }

    [TestMethod]
    public void Build_WithAMalformedNetwork_FailsFast()
    {
        var configuration = new TrustedProxyConfiguration(
            Configuration(("ForwardedHeaders:KnownNetworks", "172.16.0.0")));

        var build = () => configuration.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*KnownNetworks*");
    }

    [TestMethod]
    public void Build_WithAMalformedProxyAddress_FailsFast()
    {
        var configuration = new TrustedProxyConfiguration(
            Configuration(("ForwardedHeaders:KnownProxies", "not-an-ip")));

        var build = () => configuration.Build();

        build.Should().Throw<InvalidOperationException>().WithMessage("*KnownProxies*");
    }
}
