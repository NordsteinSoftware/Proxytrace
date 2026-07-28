using AwesomeAssertions;
using Proxytrace.Api.Configuration;

namespace Proxytrace.Api.Tests.Config;

/// <summary>
/// Pins the environment-name resolution the container module shares with the host. The precedence
/// is the whole point: <c>WebApplicationBuilder</c> lets <c>DOTNET_ENVIRONMENT</c> win over
/// <c>ASPNETCORE_ENVIRONMENT</c>, and reading them the other way round defaulted the session
/// cookie's <c>Secure</c> attribute to <c>false</c> on an HTTPS install.
/// </summary>
[TestClass]
public sealed class HostEnvironmentNameTests
{
    private static Func<string, string?> Environment(string? dotnet = null, string? aspNetCore = null) =>
        name => name switch
        {
            "DOTNET_ENVIRONMENT" => dotnet,
            "ASPNETCORE_ENVIRONMENT" => aspNetCore,
            _ => null,
        };

    [TestMethod]
    public void Resolve_WithNeitherVariableSet_IsProduction() =>
        HostEnvironmentName.Resolve(Environment()).Should().Be("Production");

    [TestMethod]
    public void Resolve_WithOnlyAspNetCoreSet_UsesIt() =>
        HostEnvironmentName.Resolve(Environment(aspNetCore: "Staging")).Should().Be("Staging");

    [TestMethod]
    public void Resolve_WithOnlyDotnetSet_UsesIt() =>
        HostEnvironmentName.Resolve(Environment(dotnet: "Staging")).Should().Be("Staging");

    [TestMethod]
    public void Resolve_WhenBothSetAndDisagree_PrefersDotnetLikeTheHost()
    {
        HostEnvironmentName.Resolve(Environment(dotnet: "Production", aspNetCore: "Development"))
            .Should().Be("Production");

        HostEnvironmentName.Resolve(Environment(dotnet: "Development", aspNetCore: "Production"))
            .Should().Be("Development");
    }

    [TestMethod]
    public void Resolve_WithBlankValues_TreatsThemAsUnset()
    {
        HostEnvironmentName.Resolve(Environment(dotnet: "", aspNetCore: "Development"))
            .Should().Be("Development");

        HostEnvironmentName.Resolve(Environment(dotnet: "   ", aspNetCore: "  "))
            .Should().Be("Production");
    }

    [TestMethod]
    public void IsDevelopment_MatchesCaseInsensitively()
    {
        HostEnvironmentName.IsDevelopment("development").Should().BeTrue();
        HostEnvironmentName.IsDevelopment("Development").Should().BeTrue();
        HostEnvironmentName.IsDevelopment("Production").Should().BeFalse();
        HostEnvironmentName.IsDevelopment("Staging").Should().BeFalse();
    }
}
