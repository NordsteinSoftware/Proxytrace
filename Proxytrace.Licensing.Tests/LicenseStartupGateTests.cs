using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nordstein.Core.Common.Async;
using Proxytrace.Licensing.Internal;
using Nordstein.Core.Testing;

namespace Proxytrace.Licensing.Tests;

[TestClass]
public sealed class LicenseStartupGateTests : BaseTest<Module>
{
    /// <summary>
    /// Constructs a <see cref="LicenseService"/> in isolation with the given configuration,
    /// bypassing AutoActivate so we can assert directly on startup-resolution outcomes.
    /// </summary>
    private static LicenseService Create(LicensingConfiguration config)
    {
        var validator = new JwtLicenseValidator(config, NullLogger<JwtLicenseValidator>.Instance);
        var resolver = new ConfiguredLicenseResolver(config, validator, NullLogger<ConfiguredLicenseResolver>.Instance);
        var trigger = Substitute.For<ILicenseRefreshTrigger>();
        return new LicenseService(resolver, NoOpLock(), () => trigger, NullLogger<LicenseService>.Instance);
    }

    private static IAsyncLock NoOpLock()
    {
        var gate = Substitute.For<IAsyncLock>();
        gate.Lock(Arg.Any<object>()).Returns(Substitute.For<IDisposable>());
        gate.LockAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IDisposable>()));
        return gate;
    }

    [TestMethod]
    public void Construct_NoJwt_RunsFree()
    {
        var service = Create(Module.Factory.Configuration(jwt: null));

        service.Current.Tier.Should().Be(LicenseTier.Free);
        service.Current.Status.Should().Be(LicenseStatus.Free);
        service.Current.Source.Should().Be(LicenseSource.None);
    }

    [TestMethod]
    public void Construct_ValidJwt_RunsActive()
    {
        var service = Create(Module.Factory.Configuration(Module.Factory.CreateJwt(tier: "Enterprise")));

        service.Current.Tier.Should().Be(LicenseTier.Enterprise);
        service.Current.Status.Should().Be(LicenseStatus.Active);
        service.Current.Source.Should().Be(LicenseSource.Environment);
        service.IsFeatureEnabled(LicenseFeature.AgenticEvaluators).Should().BeTrue();
    }

    [TestMethod]
    public void Construct_MalformedJwt_DegradesToInvalidFree()
        => AssertInvalid(Create(Module.Factory.Configuration("garbage")));

    [TestMethod]
    public void Construct_BadSignature_DegradesToInvalidFree()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(sign: false))));

    [TestMethod]
    public void Construct_WrongIssuer_DegradesToInvalidFree()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(issuer: "https://evil.example.com"))));

    [TestMethod]
    public void Construct_WrongAudience_DegradesToInvalidFree()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(audience: "nope"))));

    [TestMethod]
    public void Construct_ExpiredJwt_DegradesToInvalidFree()
        => AssertInvalid(Create(Module.Factory.Configuration(Module.Factory.CreateJwt(expires: DateTimeOffset.UtcNow.AddMinutes(-1)))));

    /// <summary>
    /// An invalid configured license must never crash the host: it boots with Free-tier
    /// entitlements, LicenseStatus.Invalid, and the rejection reason for the UI.
    /// </summary>
    private static void AssertInvalid(LicenseService service)
    {
        service.Current.Tier.Should().Be(LicenseTier.Free);
        service.Current.Status.Should().Be(LicenseStatus.Invalid);
        service.Current.Source.Should().Be(LicenseSource.Environment);
        service.Current.InvalidReason.Should().NotBeNullOrEmpty();
        service.IsFeatureEnabled(LicenseFeature.AgenticEvaluators).Should().BeFalse();
    }

    [TestMethod]
    public async Task ForceRefresh_DelegatesToRefreshTrigger()
    {
        var trigger = Substitute.For<ILicenseRefreshTrigger>();
        var config = Module.Factory.Configuration(Module.Factory.CreateJwt());
        var validator = new JwtLicenseValidator(config, NullLogger<JwtLicenseValidator>.Instance);
        var resolver = new ConfiguredLicenseResolver(config, validator, NullLogger<ConfiguredLicenseResolver>.Instance);
        var service = new LicenseService(resolver, NoOpLock(), () => trigger, NullLogger<LicenseService>.Instance);

        await service.ForceRefreshAsync(CancellationToken);

        await trigger.Received(1).RunCheckNowAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void GetServices_FreeTier_LicenseServiceResolvesToFree()
    {
        var services = GetServices();
        var licenseService = services.GetRequiredService<ILicenseService>();

        licenseService.Current.Status.Should().Be(LicenseStatus.Free);
    }

    [TestMethod]
    public void GetServices_WithEnterpriseJwt_LicenseServiceResolvesToActive()
    {
        var jwt = Module.Factory.CreateJwt(tier: "Enterprise");
        var config = Module.Factory.Configuration(jwt);

        var services = GetServices(builder =>
            builder.RegisterInstance(config).SingleInstance());

        var licenseService = services.GetRequiredService<ILicenseService>();
        licenseService.Current.Status.Should().Be(LicenseStatus.Active);
        licenseService.IsFeatureEnabled(LicenseFeature.OptimizationProposals).Should().BeTrue();
    }
}
