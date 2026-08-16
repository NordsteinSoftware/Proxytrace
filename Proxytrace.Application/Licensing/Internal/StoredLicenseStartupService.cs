using Proxytrace.Domain.Licensing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Licensing.Internal;

/// <summary>
/// Applies the database-stored license (set via the setup wizard or settings UI) once the
/// database is available. Registered after the database initializer so migrations have run.
/// A stored license takes precedence over the environment-configured one resolved at container
/// build; when none is stored the startup snapshot is kept. Never fails the host — a failure
/// here only means the deployment keeps running on the environment license or Free.
/// </summary>
internal sealed class StoredLicenseStartupService : IHostedService
{
    private readonly IStoredLicenseStore store;
    private readonly ILicenseActivator activator;
    private readonly ILicenseService licenseService;
    private readonly ILogger<StoredLicenseStartupService> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoredLicenseStartupService"/> class.
    /// </summary>
    public StoredLicenseStartupService(
        IStoredLicenseStore store,
        ILicenseActivator activator,
        ILicenseService licenseService,
        ILogger<StoredLicenseStartupService> logger)
    {
        this.store = store;
        this.activator = activator;
        this.licenseService = licenseService;
        this.logger = logger;
    }

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Kiosk/demo deployments run on a fixed override snapshot; never replace it.
        if (licenseService.Current.Source == LicenseSource.Override)
            return;

        try
        {
            var stored = await store.GetAsync(cancellationToken);
            if (stored is null)
                return;

            // ActivateOrInvalid: an expired/rejected stored license surfaces as Invalid in the
            // UI (with Free entitlements) instead of being silently ignored, so an admin can
            // correct it in the settings.
            activator.ActivateOrInvalid(stored, LicenseSource.Stored);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load the stored license; keeping the startup license");
        }
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
