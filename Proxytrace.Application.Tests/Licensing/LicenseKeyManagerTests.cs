using Proxytrace.Domain.Licensing;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Licensing;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Licensing;

[TestClass]
public sealed class LicenseKeyManagerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task SetAsync_ValidKey_PersistsAndActivates()
    {
        var activator = Substitute.For<ILicenseActivator>();
        activator.Validate("jwt").Returns(LicenseSnapshot.Free());
        var services = GetServices(builder =>
            builder.RegisterInstance(activator).As<ILicenseActivator>());
        var manager = services.GetRequiredService<ILicenseKeyManager>();
        var store = services.GetRequiredService<IStoredLicenseStore>();

        await manager.SetAsync("jwt", CancellationToken);

        (await store.GetAsync(CancellationToken)).Should().Be("jwt");
        activator.Received(1).Activate("jwt", LicenseSource.Stored);
    }

    [TestMethod]
    public async Task SetAsync_InvalidKey_StoresNothingAndKeepsLicense()
    {
        var activator = Substitute.For<ILicenseActivator>();
        activator.Validate("bad").Returns(_ => throw new InvalidLicenseException(InvalidLicenseReason.Malformed));
        var services = GetServices(builder =>
            builder.RegisterInstance(activator).As<ILicenseActivator>());
        var manager = services.GetRequiredService<ILicenseKeyManager>();
        var store = services.GetRequiredService<IStoredLicenseStore>();

        await FluentActions
            .Invoking(() => manager.SetAsync("bad", CancellationToken))
            .Should().ThrowAsync<InvalidLicenseException>();

        (await store.GetAsync(CancellationToken)).Should().BeNull();
        activator.DidNotReceive().Activate(Arg.Any<string>(), Arg.Any<LicenseSource>());
    }

    [TestMethod]
    public async Task SetAsync_ReplacesPreviouslyStoredKey()
    {
        var activator = Substitute.For<ILicenseActivator>();
        activator.Validate(Arg.Any<string>()).Returns(LicenseSnapshot.Free());
        var services = GetServices(builder =>
            builder.RegisterInstance(activator).As<ILicenseActivator>());
        var manager = services.GetRequiredService<ILicenseKeyManager>();
        var store = services.GetRequiredService<IStoredLicenseStore>();

        await manager.SetAsync("first", CancellationToken);
        await manager.SetAsync("second", CancellationToken);

        (await store.GetAsync(CancellationToken)).Should().Be("second");
    }

    [TestMethod]
    public async Task RemoveAsync_RemovesStoredKeyAndActivatesConfigured()
    {
        var activator = Substitute.For<ILicenseActivator>();
        var services = GetServices(builder =>
            builder.RegisterInstance(activator).As<ILicenseActivator>());
        var manager = services.GetRequiredService<ILicenseKeyManager>();
        var store = services.GetRequiredService<IStoredLicenseStore>();
        await store.SaveAsync("jwt", CancellationToken);

        await manager.RemoveAsync(CancellationToken);

        (await store.GetAsync(CancellationToken)).Should().BeNull();
        activator.Received(1).ActivateConfigured();
    }

    [TestMethod]
    public async Task SetAsync_OverrideLicense_Throws()
    {
        // Kiosk/demo deployments run on a fixed override snapshot; the license cannot be managed.
        var licenseService = Substitute.For<ILicenseService>();
        licenseService.Current.Returns(LicenseSnapshot.Enterprise("kiosk@proxytrace.dev"));
        var services = GetServices(builder =>
            builder.RegisterInstance(licenseService).As<ILicenseService>());
        var manager = services.GetRequiredService<ILicenseKeyManager>();
        var store = services.GetRequiredService<IStoredLicenseStore>();

        await FluentActions
            .Invoking(() => manager.SetAsync("jwt", CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();

        (await store.GetAsync(CancellationToken)).Should().BeNull();
    }
}
