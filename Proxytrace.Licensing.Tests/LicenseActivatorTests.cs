using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Licensing.Exceptions;
using Proxytrace.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Licensing.Tests;

[TestClass]
public sealed class LicenseActivatorTests : BaseTest<Module>
{
    [TestMethod]
    public void Validate_InvalidJwt_Throws()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();

        var action = () => activator.Validate("garbage");

        action.Should().Throw<InvalidLicenseException>()
            .Which.Reason.Should().Be(InvalidLicenseReason.Malformed);
    }

    [TestMethod]
    public void Validate_ValidJwt_ReturnsSnapshotWithoutApplying()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        var snapshot = activator.Validate(Module.Factory.CreateJwt(tier: "Enterprise"));

        snapshot.Tier.Should().Be(LicenseTier.Enterprise);
        license.Current.Tier.Should().Be(LicenseTier.Free, "validation must not change the active license");
    }

    [TestMethod]
    public void Activate_ValidJwt_AppliesSnapshotWithSource()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        activator.Activate(Module.Factory.CreateJwt(tier: "Enterprise"), LicenseSource.Stored);

        license.Current.Tier.Should().Be(LicenseTier.Enterprise);
        license.Current.Status.Should().Be(LicenseStatus.Active);
        license.Current.Source.Should().Be(LicenseSource.Stored);
    }

    [TestMethod]
    public void Activate_InvalidJwt_ThrowsAndKeepsCurrentLicense()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(tier: "Enterprise"), LicenseSource.Stored);

        var action = () => activator.Activate("garbage", LicenseSource.Stored);

        action.Should().Throw<InvalidLicenseException>();
        license.Current.Tier.Should().Be(LicenseTier.Enterprise, "a rejected JWT must not replace the active license");
    }

    [TestMethod]
    public void ActivateOrInvalid_ExpiredJwt_AppliesInvalidFreeSnapshot()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();

        var expired = Module.Factory.CreateJwt(expires: DateTimeOffset.UtcNow.AddMinutes(-1));
        var snapshot = activator.ActivateOrInvalid(expired, LicenseSource.Stored);

        snapshot.Status.Should().Be(LicenseStatus.Invalid);
        license.Current.Tier.Should().Be(LicenseTier.Free);
        license.Current.Status.Should().Be(LicenseStatus.Invalid);
        license.Current.Source.Should().Be(LicenseSource.Stored);
        license.Current.InvalidReason.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void ActivateConfigured_NoEnvironmentJwt_RevertsToFree()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(tier: "Enterprise"), LicenseSource.Stored);

        activator.ActivateConfigured();

        license.Current.Tier.Should().Be(LicenseTier.Free);
        license.Current.Source.Should().Be(LicenseSource.None);
    }

    [TestMethod]
    public void ActivateConfigured_WithEnvironmentJwt_RevertsToEnvironmentLicense()
    {
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt(subject: "env@example.com"));
        var services = GetServices(builder => builder.RegisterInstance(config).SingleInstance());
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        activator.Activate(Module.Factory.CreateJwt(subject: "stored@example.com"), LicenseSource.Stored);

        activator.ActivateConfigured();

        license.Current.CustomerEmail.Should().Be("env@example.com");
        license.Current.Source.Should().Be(LicenseSource.Environment);
    }

    [TestMethod]
    public void Activate_RaisesChangedEvent()
    {
        var services = GetServices();
        var activator = services.GetRequiredService<ILicenseActivator>();
        var license = services.GetRequiredService<ILicenseService>();
        var raised = false;
        license.Changed += () => raised = true;

        activator.Activate(Module.Factory.CreateJwt(tier: "Enterprise"), LicenseSource.Stored);

        raised.Should().BeTrue();
    }
}
