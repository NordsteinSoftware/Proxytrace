using Proxytrace.Domain.Licensing;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Licensing.Internal;

internal sealed class LicenseKeyManager : ILicenseKeyManager
{
    private readonly IStoredLicenseStore store;
    private readonly ILicenseActivator activator;
    private readonly ILicenseService licenseService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseKeyManager"/> class.
    /// </summary>
    public LicenseKeyManager(
        IStoredLicenseStore store,
        ILicenseActivator activator,
        ILicenseService licenseService)
    {
        this.store = store;
        this.activator = activator;
        this.licenseService = licenseService;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public LicenseSnapshot Validate(string licenseJwt)
        => activator.Validate(licenseJwt);

    /// <summary>
    /// Sets asynchronously.
    /// </summary>
    public async Task<LicenseSnapshot> SetAsync(string licenseJwt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseJwt);
        EnsureManageable();

        // Validate before persisting so a rejected JWT never replaces the stored license.
        activator.Validate(licenseJwt);

        await store.SaveAsync(licenseJwt.Trim(), cancellationToken);
        return activator.Activate(licenseJwt.Trim(), LicenseSource.Stored);
    }

    /// <summary>
    /// Removes asynchronously.
    /// </summary>
    public async Task<LicenseSnapshot> RemoveAsync(CancellationToken cancellationToken = default)
    {
        EnsureManageable();

        await store.RemoveAsync(cancellationToken);
        return activator.ActivateConfigured();
    }

    private void EnsureManageable()
    {
        // Kiosk/demo deployments run on a fixed override snapshot; the license is not
        // user-manageable there.
        if (licenseService.Current.Source == LicenseSource.Override)
            throw new InvalidOperationException("The license is managed by the deployment and cannot be changed here.");
    }
}
