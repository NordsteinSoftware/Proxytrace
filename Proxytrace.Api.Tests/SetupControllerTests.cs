using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Setup;
using Proxytrace.Application.Cleanup;
using Proxytrace.Application.Setup;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class SetupControllerTests : BaseTest<Module>
{
    private static SetupController CreateController(
        IServiceProvider services,
        bool isDevelopment = false,
        ISetupService? setup = null) => new(
        services.GetRequiredService<IRepository<IUser>>(),
        services.GetRequiredService<IRepository<IProject>>(),
        services.GetRequiredService<IDataCleanupService>(),
        setup ?? services.GetRequiredService<ISetupService>(),
        NullLogger<Audit>.Instance,
        NullLogger<SetupController>.Instance,
        Env(isDevelopment));

    private static IWebHostEnvironment Env(bool isDevelopment)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return env;
    }

    [TestMethod]
    public async Task GetStatus_WhenNoUsersOrProjectsExist_ReturnsNotConfigured()
    {
        IServiceProvider services = GetServices();
        var controller = CreateController(services);

        var result = await controller.GetStatus(CancellationToken);

        result.IsConfigured.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetStatus_WhenUserButNoProject_ReturnsNotConfigured()
    {
        IServiceProvider services = GetServices();
        await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);

        var controller = CreateController(services);

        var result = await controller.GetStatus(CancellationToken);

        result.IsConfigured.Should().BeFalse();
    }

    [TestMethod]
    public async Task GetStatus_WhenUserAndProjectExist_ReturnsConfigured()
    {
        IServiceProvider services = GetServices();
        await services.GetRequiredService<IDomainEntityGenerator<IUser>>().CreateAsync(CancellationToken);
        await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var controller = CreateController(services);

        var result = await controller.GetStatus(CancellationToken);

        result.IsConfigured.Should().BeTrue();
    }

    [TestMethod]
    public async Task TestConnection_SchemelessEndpoint_DefaultsToHttps()
    {
        IServiceProvider services = GetServices();
        var setup = Substitute.For<ISetupService>();
        setup.TestProviderConnectionAsync(Arg.Any<ProviderConnectionInput>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderConnectionResult(true, null, 3));
        var controller = CreateController(services, setup: setup);

        var result = await controller.TestConnection(
            new TestConnectionRequest("p", "provider.example/v1", "key", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        result.Success.Should().BeTrue();
        result.ModelCount.Should().Be(3);
        await setup.Received(1).TestProviderConnectionAsync(
            Arg.Is<ProviderConnectionInput>(i => i != null && i.ProviderEndpoint == new Uri("https://provider.example/v1")),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TestConnection_WhenProviderRejectsKey_ReturnsClassifiedFailure()
    {
        IServiceProvider services = GetServices();
        var setup = Substitute.For<ISetupService>();
        setup.TestProviderConnectionAsync(Arg.Any<ProviderConnectionInput>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderConnectionResult(false, ProviderConnectionError.Unauthorized, 0));
        var controller = CreateController(services, setup: setup);

        var result = await controller.TestConnection(
            new TestConnectionRequest("p", "https://provider.example", "wrong-key", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(nameof(ProviderConnectionError.Unauthorized));
        result.ModelCount.Should().Be(0);
        result.Error.Should().BeNull();
    }

    [TestMethod]
    public async Task TestConnection_WhenCallerCancels_PropagatesCancellation()
    {
        IServiceProvider services = GetServices();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var setup = Substitute.For<ISetupService>();
        setup.TestProviderConnectionAsync(Arg.Any<ProviderConnectionInput>(), cancellation.Token)
            .Returns<ProviderConnectionResult>(_ => throw new OperationCanceledException(cancellation.Token));
        var controller = CreateController(services, setup: setup);

        await FluentActions
            .Invoking(() => controller.TestConnection(
                new TestConnectionRequest("p", "https://provider.example", "key", ModelProviderKind.OpenAiCompatible),
                cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task TestConnection_WhenSetupThrows_OutsideDevelopment_SuppressesRawMessage()
    {
        IServiceProvider services = GetServices();
        var setup = Substitute.For<ISetupService>();
        setup.TestProviderConnectionAsync(Arg.Any<ProviderConnectionInput>(), Arg.Any<CancellationToken>())
            .Returns<ProviderConnectionResult>(_ => throw new InvalidOperationException("connect failed at db host 10.0.0.5"));
        var controller = CreateController(services, setup: setup);

        var result = await controller.TestConnection(
            new TestConnectionRequest("p", "https://provider.example", "key", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        result.Success.Should().BeFalse();
        // Outside Development the raw exception message (host/credential/internal detail) must not leak.
        result.Error.Should().BeNull();
        result.ErrorId.Should().NotBeNull();
        result.ErrorCode.Should().Be(nameof(ProviderConnectionError.Unknown));
    }

    [TestMethod]
    public async Task TestConnection_WhenSetupThrows_InDevelopment_KeepsRawMessage()
    {
        IServiceProvider services = GetServices();
        var setup = Substitute.For<ISetupService>();
        setup.TestProviderConnectionAsync(Arg.Any<ProviderConnectionInput>(), Arg.Any<CancellationToken>())
            .Returns<ProviderConnectionResult>(_ => throw new InvalidOperationException("connect failed at db host 10.0.0.5"));
        var controller = CreateController(services, isDevelopment: true, setup: setup);

        var result = await controller.TestConnection(
            new TestConnectionRequest("p", "https://provider.example", "key", ModelProviderKind.OpenAiCompatible),
            CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("connect failed at db host 10.0.0.5");
        result.ErrorId.Should().NotBeNull();
        result.ErrorCode.Should().Be(nameof(ProviderConnectionError.Unknown));
    }
}
