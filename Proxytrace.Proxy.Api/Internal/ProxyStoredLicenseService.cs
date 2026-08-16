using Autofac;
using Proxytrace.Domain.Licensing;
using Proxytrace.Licensing;

namespace Proxytrace.Proxy.Api.Internal;

/// <summary>
/// Keeps the proxy's license snapshot in sync with the database-stored license (set via the main
/// app's setup wizard or settings UI). Mirrors the app's <c>StoredLicenseStartupService</c>, but as
/// a polling loop: the proxy is a separate process, so it cannot hear the app's in-process license
/// <c>Changed</c> event — it re-reads the stored JWT periodically and re-activates on change
/// (removal falls back to the environment-configured license or Free). The proxy runs with
/// <c>ServerCheckEnabled = false</c> (the app owns the license-server heartbeat and the shared
/// offline-grace cache file), so a revoked-but-unexpired license stays active here until it expires
/// or the stored key is removed. Never fails the host — on errors the current snapshot is kept.
/// </summary>
internal sealed class ProxyStoredLicenseService : BackgroundService
{
    private readonly ILifetimeScope rootScope;
    private readonly ILicenseActivator activator;
    private readonly ILicenseService licenseService;
    private readonly TimeSpan pollInterval;
    private readonly ILogger<ProxyStoredLicenseService> logger;

    private string? appliedJwt;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProxyStoredLicenseService"/> class.
    /// </summary>
    public ProxyStoredLicenseService(
        ILifetimeScope rootScope,
        ILicenseActivator activator,
        ILicenseService licenseService,
        TimeSpan pollInterval,
        ILogger<ProxyStoredLicenseService> logger)
    {
        this.rootScope = rootScope;
        this.activator = activator;
        this.licenseService = licenseService;
        this.pollInterval = pollInterval;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kiosk/demo deployments run on a fixed override snapshot; never replace it.
        if (licenseService.Current.Source == LicenseSource.Override)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOnceAsync(stoppingToken);
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SyncOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Resolve the store in a child scope per poll: its repository takes a per-call
            // StorageDbContext that would otherwise accumulate on the root scope for the process
            // lifetime (the singleton-hosted-service context-leak gotcha, see docs/database.md).
            string? stored;
            await using (var scope = rootScope.BeginLifetimeScope())
            {
                stored = await scope.Resolve<IStoredLicenseStore>().GetAsync(cancellationToken);
            }

            if (stored == appliedJwt)
            {
                return;
            }

            if (stored is null)
            {
                activator.ActivateConfigured();
                logger.LogInformation("Stored license removed; reverted to the configured license");
            }
            else
            {
                // ActivateOrInvalid: an expired/rejected stored license degrades to Free
                // entitlements (blocking off) instead of throwing out of the poll loop.
                var snapshot = activator.ActivateOrInvalid(stored, LicenseSource.Stored);
                logger.LogInformation(
                    "Applied stored license (tier {Tier}, status {Status})", snapshot.Tier, snapshot.Status);
            }

            appliedJwt = stored;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to sync the stored license; keeping the current snapshot");
        }
    }
}
