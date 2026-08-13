using Proxytrace.Domain.AuditLog;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.License;
using Proxytrace.Application.Ingestion;
using Proxytrace.Application.Licensing;
using Proxytrace.Application.Setup;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class LicenseControllerTests : BaseTest<Module>
{
    private static LicenseController ResolveController(
        IServiceProvider services,
        ILicenseService licenseService,
        ILicenseKeyManager? keyManager = null,
        bool usersExist = true,
        bool authenticatedAsAdmin = false)
    {
        var quotaGuard = services.GetRequiredService<ITraceQuotaGuard>();
        var setup = Substitute.For<ISetupService>();
        setup.AnyUsersExistAsync(Arg.Any<CancellationToken>()).Returns(usersExist);

        var controller = new LicenseController(
            licenseService,
            keyManager ?? Substitute.For<ILicenseKeyManager>(),
            setup,
            quotaGuard,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);

        var identity = authenticatedAsAdmin
            ? new ClaimsIdentity([new Claim(ClaimTypes.Role, nameof(UserRole.Admin))], "test")
            : new ClaimsIdentity();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };

        return controller;
    }

    private static ILicenseService StubLicense(LicenseSnapshot snapshot)
    {
        var service = Substitute.For<ILicenseService>();
        service.Current.Returns(snapshot);
        service.IsFeatureEnabled(Arg.Any<LicenseFeature>())
            .Returns(call => snapshot.Features.Contains(call.Arg<LicenseFeature>()));
        service.GetLimit(Arg.Any<LicenseLimit>())
            .Returns(call => snapshot.Limits.TryGetValue(call.Arg<LicenseLimit>(), out var v) ? v : 0);
        return service;
    }

    [TestMethod]
    public void Get_FreeTier_ReturnsFreeDto()
    {
        var services = GetServices();
        var controller = ResolveController(services, StubLicense(LicenseSnapshot.Free()));

        var dto = controller.Get();

        dto.Tier.Should().Be("free");
        dto.Status.Should().Be("free");
        dto.Source.Should().Be("none");
        dto.Features.Should().BeEmpty();
        dto.Limits.Should().ContainKey(nameof(LicenseLimit.MaxProjects));
    }

    [TestMethod]
    public void Get_ActiveEnterprise_ReturnsEnterpriseDto()
    {
        var definition = LicensePolicy.For(LicenseTier.Enterprise);
        var snapshot = new LicenseSnapshot(
            LicenseTier.Enterprise,
            LicenseStatus.Active,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            "customer@example.com",
            "jti-1",
            definition.Features,
            definition.Limits,
            LicenseSource.Stored);

        var services = GetServices();
        var dto = ResolveController(services, StubLicense(snapshot), authenticatedAsAdmin: true).Get();

        dto.Tier.Should().Be("enterprise");
        dto.Status.Should().Be("active");
        dto.Source.Should().Be("stored");
        dto.CustomerEmail.Should().Be("customer@example.com");
        dto.Features.Should().Contain(nameof(LicenseFeature.OptimizationProposals));
    }

    [TestMethod]
    public void Get_Anonymously_WithholdsTheCustomerEmail()
    {
        // The endpoint is deliberately anonymous (the setup wizard and sign-in screen need the tier
        // before any user exists), so it must not disclose who the licence belongs to — otherwise a
        // plain unauthenticated GET yields the purchaser's email address.
        var definition = LicensePolicy.For(LicenseTier.Enterprise);
        var snapshot = new LicenseSnapshot(
            LicenseTier.Enterprise,
            LicenseStatus.Active,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            "customer@example.com",
            "jti-1",
            definition.Features,
            definition.Limits,
            LicenseSource.Stored);

        var services = GetServices();
        var dto = ResolveController(services, StubLicense(snapshot)).Get();

        dto.CustomerEmail.Should().BeNull();
        // Everything the anonymous callers actually need is still there.
        dto.Tier.Should().Be("enterprise");
        dto.Status.Should().Be("active");
    }

    [TestMethod]
    public void Get_OfflineLicense_ProjectsOfflineFlag()
    {
        var definition = LicensePolicy.For(LicenseTier.Enterprise);
        var snapshot = new LicenseSnapshot(
            LicenseTier.Enterprise,
            LicenseStatus.Active,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            "airgap@example.com",
            "jti-offline",
            definition.Features,
            definition.Limits,
            LicenseSource.Stored,
            InvalidReason: null,
            Offline: true);

        var services = GetServices();
        var dto = ResolveController(services, StubLicense(snapshot)).Get();

        dto.Offline.Should().BeTrue();
    }

    [TestMethod]
    public void Get_OnlineLicense_OfflineFlagIsFalse()
    {
        var services = GetServices();
        var dto = ResolveController(services, StubLicense(LicenseSnapshot.Free())).Get();

        dto.Offline.Should().BeFalse();
    }

    [TestMethod]
    public void Validate_OfflineKey_PreviewReportsOffline()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var definition = LicensePolicy.For(LicenseTier.Enterprise);
        keyManager.Validate("jwt").Returns(new LicenseSnapshot(
            LicenseTier.Enterprise,
            LicenseStatus.Active,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            "airgap@example.com",
            "jti-offline",
            definition.Features,
            definition.Limits,
            LicenseSource.None,
            InvalidReason: null,
            Offline: true));
        var controller = ResolveController(services, StubLicense(LicenseSnapshot.Free()), keyManager);

        var result = controller.Validate(new SetLicenseRequest("jwt"));

        result.Valid.Should().BeTrue();
        result.Offline.Should().BeTrue();
    }

    [TestMethod]
    public void Get_InvalidStatus_ReturnsInvalidDtoWithReason()
    {
        var snapshot = LicenseSnapshot.Invalid(LicenseSource.Environment, "The configured license is invalid: Expired.");
        var services = GetServices();

        var dto = ResolveController(services, StubLicense(snapshot)).Get();

        dto.Status.Should().Be("invalid");
        dto.Source.Should().Be("environment");
        dto.InvalidReason.Should().NotBeNullOrEmpty();
    }

    [TestMethod]
    public void Get_ExpiredStatus_ReturnsExpiredDto()
    {
        var snapshot = LicenseSnapshot.Free() with { Status = LicenseStatus.Expired };
        var services = GetServices();
        var dto = ResolveController(services, StubLicense(snapshot)).Get();

        dto.Status.Should().Be("expired");
    }

    [TestMethod]
    public async Task Set_AsAdmin_SetsLicense()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var controller = ResolveController(
            services,
            StubLicense(LicenseSnapshot.Free()),
            keyManager,
            usersExist: true,
            authenticatedAsAdmin: true);

        var result = await controller.Set(new SetLicenseRequest("jwt"), CancellationToken);

        result.Value.Should().NotBeNull();
        await keyManager.Received(1).SetAsync("jwt", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Set_AnonymousBeforeSetup_SetsLicense()
    {
        // The setup wizard's first step runs before any user exists; setting the license must
        // be possible there — the same gate as first-admin creation.
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var controller = ResolveController(
            services,
            StubLicense(LicenseSnapshot.Free()),
            keyManager,
            usersExist: false,
            authenticatedAsAdmin: false);

        await controller.Set(new SetLicenseRequest("jwt"), CancellationToken);

        await keyManager.Received(1).SetAsync("jwt", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Set_AnonymousAfterSetup_Forbids()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var controller = ResolveController(
            services,
            StubLicense(LicenseSnapshot.Free()),
            keyManager,
            usersExist: true,
            authenticatedAsAdmin: false);

        var result = await controller.Set(new SetLicenseRequest("jwt"), CancellationToken);

        result.Result.Should().BeOfType<ForbidResult>();
        await keyManager.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Set_OverrideSource_ReturnsConflict()
    {
        // Kiosk/demo deployments run on a fixed override snapshot; the license is not manageable.
        var services = GetServices();
        var controller = ResolveController(
            services,
            StubLicense(LicenseSnapshot.Enterprise("kiosk@proxytrace.dev")),
            usersExist: true,
            authenticatedAsAdmin: true);

        var result = await controller.Set(new SetLicenseRequest("jwt"), CancellationToken);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [TestMethod]
    public async Task Remove_AsAdmin_RemovesStoredLicense()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var controller = ResolveController(
            services,
            StubLicense(LicenseSnapshot.Free()),
            keyManager,
            authenticatedAsAdmin: true);

        await controller.Remove(CancellationToken);

        await keyManager.Received(1).RemoveAsync(Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void Validate_ValidKey_ReturnsPreview()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        var definition = LicensePolicy.For(LicenseTier.Enterprise);
        keyManager.Validate("jwt").Returns(new LicenseSnapshot(
            LicenseTier.Enterprise,
            LicenseStatus.Active,
            DateTimeOffset.UtcNow.AddDays(30),
            null,
            "customer@example.com",
            "jti-1",
            definition.Features,
            definition.Limits));
        var controller = ResolveController(services, StubLicense(LicenseSnapshot.Free()), keyManager);

        var result = controller.Validate(new SetLicenseRequest("jwt"));

        result.Valid.Should().BeTrue();
        result.Tier.Should().Be("enterprise");
        result.CustomerEmail.Should().Be("customer@example.com");
    }

    [TestMethod]
    public void Validate_InvalidKey_ReturnsReason()
    {
        var services = GetServices();
        var keyManager = Substitute.For<ILicenseKeyManager>();
        keyManager.Validate("bad").Returns(_ => throw new InvalidLicenseException(InvalidLicenseReason.Malformed));
        var controller = ResolveController(services, StubLicense(LicenseSnapshot.Free()), keyManager);

        var result = controller.Validate(new SetLicenseRequest("bad"));

        result.Valid.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
        result.Tier.Should().BeNull();
    }

    [TestMethod]
    public async Task Refresh_CallsForceRefreshAndReturnsUpdatedDto()
    {
        var snapshot = LicenseSnapshot.Free();
        var licenseService = StubLicense(snapshot);

        var services = GetServices();
        var controller = ResolveController(services, licenseService);

        await controller.Refresh(CancellationToken);

        await licenseService.Received(1).ForceRefreshAsync(Arg.Any<CancellationToken>());
    }
}
