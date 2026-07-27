using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Dto.License;
using Proxytrace.Application.Ingestion;
using Proxytrace.Application.Licensing;
using Proxytrace.Application.Setup;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly ILicenseService licenseService;
    private readonly ILicenseKeyManager keyManager;
    private readonly ISetupService setup;
    private readonly ITraceQuotaGuard quotaGuard;
    private readonly ILogger<Audit> audit;

    public LicenseController(
        ILicenseService licenseService,
        ILicenseKeyManager keyManager,
        ISetupService setup,
        ITraceQuotaGuard quotaGuard,
        ILogger<Audit> audit)
    {
        this.licenseService = licenseService;
        this.keyManager = keyManager;
        this.setup = setup;
        this.quotaGuard = quotaGuard;
        this.audit = audit;
    }

    /// <summary>
    /// The current license. Anonymous by design — the setup wizard and the login screen need the
    /// tier before any user exists — but the licensee's identity is withheld from unauthenticated
    /// callers: without that, <c>curl https://host/api/license</c> from the internet discloses the
    /// purchaser's email address. Signed-in callers (the Settings → License panel) still see it.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public LicenseDto Get()
    {
        LicenseDto dto = Map(licenseService.Current);
        return User.Identity?.IsAuthenticated == true
            ? dto
            : dto with { CustomerEmail = null };
    }

    /// <summary>
    /// Validates a license key without storing or applying it. Anonymous by design: it is a
    /// pure offline JWT verification and the setup wizard runs before any user exists.
    /// </summary>
    [HttpPost("validate")]
    [AllowAnonymous]
    public ValidateLicenseResultDto Validate([FromBody] SetLicenseRequest request)
    {
        try
        {
            var snapshot = keyManager.Validate(request.License);
            return new ValidateLicenseResultDto(
                Valid: true,
                Reason: null,
                Tier: snapshot.Tier.ToString().ToLowerInvariant(),
                ExpiresAt: snapshot.ExpiresAt,
                CustomerEmail: snapshot.CustomerEmail,
                Offline: snapshot.Offline);
        }
        catch (InvalidLicenseException ex)
        {
            return new ValidateLicenseResultDto(
                Valid: false,
                Reason: ex.Message,
                Tier: null,
                ExpiresAt: null,
                CustomerEmail: null,
                Offline: false);
        }
    }

    /// <summary>
    /// Sets the installation's license key. Admin-only once users exist; anonymous while setup
    /// is incomplete (no users yet) so the setup wizard's first step can apply a license —
    /// the same gate as first-admin creation.
    /// </summary>
    [HttpPut]
    [AllowAnonymous]
    public async Task<ActionResult<LicenseDto>> Set(
        [FromBody] SetLicenseRequest request,
        CancellationToken cancellationToken)
    {
        if (licenseService.Current.Source == LicenseSource.Override)
            return Conflict("The license is managed by the deployment and cannot be changed here.");

        if (!await CanManageAsync(cancellationToken))
            return Forbid();

        // An invalid key throws InvalidLicenseException → 422 via the exception mapper.
        await keyManager.SetAsync(request.License, cancellationToken);

        var current = licenseService.Current;
        audit.LogAudit(
            AuditAction.LicenseSet,
            targetType: "License",
            targetLabel: current.Tier.ToString(),
            details: JsonSerializer.Serialize(new { tier = current.Tier.ToString(), customerEmail = current.CustomerEmail }));
        return Map(current);
    }

    /// <summary>
    /// Removes the stored license key; the installation falls back to the environment-supplied
    /// license, or the Free tier when none is configured.
    /// </summary>
    [HttpDelete]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<ActionResult<LicenseDto>> Remove(CancellationToken cancellationToken)
    {
        if (licenseService.Current.Source == LicenseSource.Override)
            return Conflict("The license is managed by the deployment and cannot be changed here.");

        await keyManager.RemoveAsync(cancellationToken);
        audit.LogAudit(AuditAction.LicenseRemoved, targetType: "License");
        return Map(licenseService.Current);
    }

    [HttpPost("refresh")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<LicenseDto> Refresh(CancellationToken cancellationToken)
    {
        await licenseService.ForceRefreshAsync(cancellationToken);
        return Map(licenseService.Current);
    }

    private async Task<bool> CanManageAsync(CancellationToken cancellationToken)
        => User.IsInRole(nameof(UserRole.Admin))
           || !await setup.AnyUsersExistAsync(cancellationToken);

    private LicenseDto Map(LicenseSnapshot snapshot) => new(
        Tier: snapshot.Tier.ToString().ToLowerInvariant(),
        Status: snapshot.Status.ToString().ToLowerInvariant(),
        Source: snapshot.Source.ToString().ToLowerInvariant(),
        InvalidReason: snapshot.InvalidReason,
        ExpiresAt: snapshot.ExpiresAt,
        GracePeriodEndsAt: snapshot.GracePeriodEndsAt,
        CustomerEmail: snapshot.CustomerEmail,
        Features: snapshot.Features.Select(f => f.ToString()).ToArray(),
        Limits: snapshot.Limits.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
        QuotaExceeded: quotaGuard.IsCurrentMonthOverQuota,
        Offline: snapshot.Offline);
}
