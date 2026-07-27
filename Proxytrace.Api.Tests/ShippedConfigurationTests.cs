using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Proxytrace.Domain.Kiosk;

namespace Proxytrace.Api.Tests;

/// <summary>
/// Guards the defaults baked into the shipped <c>appsettings.json</c>. These matter because they are
/// what a host gets when it is started by any path other than the two blessed ones — a hand-written
/// Kubernetes manifest, a custom Dockerfile, a bare <c>dotnet Proxytrace.Api.dll</c> — none of which
/// export the environment overrides that <c>docker-compose.yml</c> and
/// <c>deploy/allinone/entrypoint.sh</c> set by hand.
/// </summary>
[TestClass]
public sealed class ShippedConfigurationTests
{
    [TestMethod]
    public void ShippedAppSettings_DoesNotEnableKiosk()
    {
        KioskOptions kiosk = ReadShippedSection<KioskOptions>("Kiosk");

        // Kiosk mode registers KioskAuthenticationHandler as the DEFAULT AND ONLY authentication
        // scheme, and that handler authenticates every request as the seeded demo user with its real
        // role — no credential is examined. It also forces in-memory storage and an Enterprise
        // license override. Shipping it on by default means any deployment that forgets
        // Kiosk__Enabled=false serves the whole API unauthenticated and loses its data on restart.
        // Kiosk consumers (docker-compose.kiosk.yml, dev.sh, the e2e/screenshot stacks) opt in.
        kiosk.Enabled.Should().BeFalse(
            "the shipped default must fail closed — kiosk mode disables authentication entirely");
    }

    private static T ReadShippedSection<T>(string section)
        where T : new()
    {
        // AppContext.BaseDirectory is the test output folder, into which the Api project's
        // appsettings.json is copied by the project reference — i.e. the very file that ships.
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        return configuration.GetSection(section).Get<T>() ?? new T();
    }
}
