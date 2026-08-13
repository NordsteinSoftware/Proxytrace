using Proxytrace.Domain.Licensing;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Licensing.Internal;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Licensing;

[TestClass]
public sealed class StoredLicenseStartupServiceTests : BaseTest<Module>
{
    private static StoredLicenseStartupService Create(
        IStoredLicenseStore store,
        ILicenseActivator activator,
        ILicenseService licenseService)
        => new(store, activator, licenseService, NullLogger<StoredLicenseStartupService>.Instance);

    private static ILicenseService Stub(LicenseSnapshot snapshot)
    {
        var service = Substitute.For<ILicenseService>();
        service.Current.Returns(snapshot);
        return service;
    }

    [TestMethod]
    public async Task StartAsync_WithStoredLicense_AppliesItOverStartupLicense()
    {
        var services = GetServices();
        var store = services.GetRequiredService<IStoredLicenseStore>();
        await store.SaveAsync("stored-jwt", CancellationToken);
        var activator = Substitute.For<ILicenseActivator>();

        await Create(store, activator, Stub(LicenseSnapshot.Free())).StartAsync(CancellationToken);

        activator.Received(1).ActivateOrInvalid("stored-jwt", LicenseSource.Stored);
    }

    [TestMethod]
    public async Task StartAsync_NoStoredLicense_KeepsStartupLicense()
    {
        var services = GetServices();
        var store = services.GetRequiredService<IStoredLicenseStore>();
        var activator = Substitute.For<ILicenseActivator>();

        await Create(store, activator, Stub(LicenseSnapshot.Free())).StartAsync(CancellationToken);

        activator.DidNotReceive().ActivateOrInvalid(Arg.Any<string>(), Arg.Any<LicenseSource>());
    }

    [TestMethod]
    public async Task StartAsync_OverrideLicense_NeverReplacesIt()
    {
        // Kiosk/demo deployments run on a fixed override snapshot; a stored license must not
        // replace it.
        var services = GetServices();
        var store = services.GetRequiredService<IStoredLicenseStore>();
        await store.SaveAsync("stored-jwt", CancellationToken);
        var activator = Substitute.For<ILicenseActivator>();

        await Create(store, activator, Stub(LicenseSnapshot.Enterprise())).StartAsync(CancellationToken);

        activator.DidNotReceive().ActivateOrInvalid(Arg.Any<string>(), Arg.Any<LicenseSource>());
    }

    [TestMethod]
    public async Task StartAsync_StoreFailure_DoesNotThrow()
    {
        // A storage failure must never fail the host — the deployment keeps running on the
        // environment license or Free.
        var store = Substitute.For<IStoredLicenseStore>();
        store.GetAsync(Arg.Any<CancellationToken>())
            .Returns<string?>(_ => throw new InvalidOperationException("database unavailable"));
        var activator = Substitute.For<ILicenseActivator>();

        await FluentActions
            .Invoking(() => Create(store, activator, Stub(LicenseSnapshot.Free())).StartAsync(CancellationToken))
            .Should().NotThrowAsync();
    }
}
