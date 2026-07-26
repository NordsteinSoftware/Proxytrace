using System.Net;
using System.Text;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Application.Updates;
using Proxytrace.Application.Updates.Internal;
using Proxytrace.Common.Hosting;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests.Updates;

[TestClass]
public sealed class UpdateCheckServiceTests : BaseTest<Module>
{
    private static void RegisterDependencies(
        ContainerBuilder builder,
        HttpMessageHandler handler,
        string currentVersion = "1.0.0")
    {
        builder.RegisterInstance(new KioskOptions()).AsSelf();

        var appVersion = Substitute.For<IAppVersion>();
        appVersion.Version.Returns(currentVersion);
        builder.RegisterInstance(appVersion).As<IAppVersion>();

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        builder.RegisterInstance(factory).As<IHttpClientFactory>();
    }

    [TestMethod]
    public async Task CheckOnce_NewerRelease_ReportsUpdateAvailable()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"tag_name":"v1.2.0","html_url":"https://github.com/SyntaktikEU/Proxytrace/releases/tag/v1.2.0"}""");
        IServiceProvider services = GetServices(builder => RegisterDependencies(builder, handler));
        var service = services.GetRequiredService<UpdateCheckService>();

        await service.CheckOnceAsync(CancellationToken);

        service.Current.UpdateAvailable.Should().BeTrue();
        service.Current.LatestVersion.Should().Be("1.2.0");
        service.Current.CurrentVersion.Should().Be("1.0.0");
        service.Current.ReleaseUrl.Should().Be("https://github.com/SyntaktikEU/Proxytrace/releases/tag/v1.2.0");
        service.Current.CheckedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task CheckOnce_SameVersion_ReportsNoUpdate()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"tag_name":"v1.0.0","html_url":null}""");
        IServiceProvider services = GetServices(builder => RegisterDependencies(builder, handler));
        var service = services.GetRequiredService<UpdateCheckService>();

        await service.CheckOnceAsync(CancellationToken);

        service.Current.UpdateAvailable.Should().BeFalse();
        service.Current.LatestVersion.Should().Be("1.0.0");
    }

    [TestMethod]
    public async Task CheckOnce_ServerError_KeepsPreviousStatus()
    {
        var handler = new StubHandler(HttpStatusCode.InternalServerError, "boom");
        IServiceProvider services = GetServices(builder => RegisterDependencies(builder, handler));
        var service = services.GetRequiredService<UpdateCheckService>();

        await service.CheckOnceAsync(CancellationToken);

        service.Current.UpdateAvailable.Should().BeFalse();
        service.Current.LatestVersion.Should().BeNull();
        service.Current.CheckedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task CheckOnce_TransportFailure_DoesNotThrow()
    {
        var handler = new FaultingHandler();
        IServiceProvider services = GetServices(builder => RegisterDependencies(builder, handler));
        var service = services.GetRequiredService<UpdateCheckService>();

        await FluentActions
            .Invoking(() => service.CheckOnceAsync(CancellationToken))
            .Should().NotThrowAsync();

        service.Current.UpdateAvailable.Should().BeFalse();
    }

    [TestMethod]
    [DataRow("1.0.1", "1.0.0", true)]
    [DataRow("1.1.0", "1.0.9", true)]
    [DataRow("2.0.0", "1.9.9", true)]
    [DataRow("1.0.0", "1.0.0", false)]
    [DataRow("1.0.0", "1.0.1", false)]
    [DataRow("1.0.0", "1.0.0-rc.1", true)]
    [DataRow("1.0.0-rc.1", "1.0.0", false)]
    [DataRow("1.0.0-rc.2", "1.0.0-rc.1", true)]
    [DataRow("1.2.3", "0.0.0-dev", true)]
    [DataRow("not-a-version", "1.0.0", false)]
    [DataRow("1.2.3", "garbage", false)]
    public void IsNewer_ComparesSemVer(string candidate, string current, bool expected)
        => UpdateCheckService.IsNewer(candidate, current).Should().Be(expected);

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FaultingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("simulated transport failure");
    }
}
