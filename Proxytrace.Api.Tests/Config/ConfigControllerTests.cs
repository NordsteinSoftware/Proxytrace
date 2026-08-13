using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Common.Hosting;

namespace Proxytrace.Api.Tests.Config;

[TestClass]
public sealed class ConfigControllerTests
{
    private static KioskEndpointOptions ConfiguredEndpoint() => new()
    {
        BaseUrl = "https://api.openai.com/v1",
        ApiKey = "sk-test",
        Model = "gpt-4o",
    };

    private static IAppVersion AppVersion(string version = "1.2.3")
    {
        var appVersion = Substitute.For<IAppVersion>();
        appVersion.Version.Returns(version);
        return appVersion;
    }

    private static ConfigController Controller(
        KioskOptions? kiosk = null,
        KioskEndpointOptions? endpoint = null,
        IAppVersion? version = null,
        IngestionProxyOptions? proxy = null)
        => new(
            kiosk ?? new KioskOptions { Enabled = false },
            endpoint ?? new KioskEndpointOptions(),
            version ?? AppVersion(),
            proxy ?? new IngestionProxyOptions());

    [TestMethod]
    public void Get_NonKiosk_InteractiveTrue()
    {
        var controller = Controller(new KioskOptions { Enabled = false });

        dynamic result = controller.Get();

        ((bool)result.kiosk).Should().BeFalse();
        ((bool)result.interactive).Should().BeTrue();
    }

    [TestMethod]
    public void Get_KioskWithEndpoint_InteractiveTrue()
    {
        var controller = Controller(new KioskOptions { Enabled = true }, ConfiguredEndpoint());

        dynamic result = controller.Get();

        ((bool)result.kiosk).Should().BeTrue();
        ((bool)result.interactive).Should().BeTrue();
    }

    [TestMethod]
    public void Get_KioskWithoutEndpoint_InteractiveFalse()
    {
        var controller = Controller(new KioskOptions { Enabled = true });

        dynamic result = controller.Get();

        ((bool)result.kiosk).Should().BeTrue();
        ((bool)result.interactive).Should().BeFalse();
    }

    [TestMethod]
    public void Get_ReturnsAppVersion()
    {
        var controller = Controller(version: AppVersion("4.5.6"));

        dynamic result = controller.Get();

        ((string)result.version).Should().Be("4.5.6");
    }

    [TestMethod]
    public void Get_WithProxyPublicBaseUrl_ReturnsIt()
    {
        var controller = Controller(
            proxy: new IngestionProxyOptions { PublicBaseUrl = "http://localhost:5102" });

        dynamic result = controller.Get();

        ((string)result.proxyBaseUrl).Should().Be("http://localhost:5102");
    }

    [TestMethod]
    public void Get_WithoutProxyPublicBaseUrl_ReturnsNull()
    {
        var controller = Controller();

        dynamic result = controller.Get();

        ((string?)result.proxyBaseUrl).Should().BeNull();
    }
}
