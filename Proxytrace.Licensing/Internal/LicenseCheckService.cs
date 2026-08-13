using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Hosting;
using Nordstein.Core.Common.Time;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// Background service running the license revocation/grace state machine. After the synchronous
/// startup gate (in <see cref="LicenseService"/>), this periodically contacts the license server
/// and degrades the tier when the server reports revocation or remains unreachable past the
/// offline grace window.
/// <para>
/// An <b>offline-only</b> license (the JWT carries <c>offline: true</c>, see
/// <see cref="LicenseSnapshot.Offline"/>) is exempt from the server check entirely — air-gapped
/// installs cannot reach the server, which is the whole point. Such a key cannot be revoked; its
/// <c>exp</c> is the only thing that ends it, and that is enforced locally here.
/// </para>
/// </summary>
internal sealed class LicenseCheckService : BackgroundService, ILicenseRefreshTrigger
{
    private readonly LicenseService licenseService;
    private readonly ILicenseServerClient serverClient;
    private readonly ILicenseCacheStore cacheStore;
    private readonly LicensingConfiguration configuration;
    private readonly IClock clock;
    private readonly IAppVersion appVersion;
    private readonly ILogger<LicenseCheckService> logger;

    private readonly SemaphoreSlim checkLock = new(1, 1);

    private readonly DateTimeOffset serviceStartedUtc;

    private volatile TaskCompletionSource licenseChanged =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LicenseCheckService(
        LicenseService licenseService,
        ILicenseServerClient serverClient,
        ILicenseCacheStore cacheStore,
        LicensingConfiguration configuration,
        IClock clock,
        IAppVersion appVersion,
        ILogger<LicenseCheckService> logger)
    {
        this.licenseService = licenseService;
        this.serverClient = serverClient;
        this.cacheStore = cacheStore;
        this.configuration = configuration;
        this.clock = clock;
        this.appVersion = appVersion;
        this.logger = logger;

        // Stable anchor for deployments that have never reached the license server: the offline
        // grace window is measured from service start so a never-connected (e.g. air-gapped)
        // deployment still degrades through Grace to Free instead of staying Active forever.
        serviceStartedUtc = clock.UtcNow;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Server checks disabled (e.g. local dev builds): keep the startup snapshot, no network.
        if (!configuration.ServerCheckEnabled)
        {
            logger.LogInformation("License server checks are disabled; keeping the startup license snapshot.");
            return;
        }

        var period = TimeSpan.FromHours(Math.Max(1, configuration.CheckIntervalHours));

        // A license can appear (or disappear) at runtime — the stored license is applied after
        // migrations, and an admin can set/remove one from the UI. React to snapshot changes
        // instead of latching onto the startup state: check immediately whenever a license with
        // a jti is active, otherwise idle until the next change or interval tick.
        licenseService.Changed += OnLicenseChanged;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = period;
                var snapshot = licenseService.Current;
                if (snapshot.Jti is not null)
                {
                    if (snapshot.Offline)
                    {
                        // Offline-only key: never contact the server (it cannot be revoked).
                        // Enforce exp locally and wake exactly when it lands so the license ends
                        // on time without any network call.
                        EnforceOfflineExpiry(snapshot);
                        delay = OfflineWakeDelay(licenseService.Current, period);
                    }
                    else
                    {
                        await SafeRunCheckAsync(cancellationToken);
                    }
                }

                var changed = licenseChanged;
                try
                {
                    await Task.WhenAny(Task.Delay(delay, cancellationToken), changed.Task);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (changed.Task.IsCompleted)
                    licenseChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
        finally
        {
            licenseService.Changed -= OnLicenseChanged;
        }
    }

    private void OnLicenseChanged() => licenseChanged.TrySetResult();

    public Task RunCheckNowAsync(CancellationToken cancellationToken) => RunCheckAsync(cancellationToken);

    private async Task SafeRunCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunCheckAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient failure must count toward the grace window, not crash the loop.
            logger.LogWarning(ex, "License check iteration failed");
        }
    }

    private async Task RunCheckAsync(CancellationToken cancellationToken)
    {
        var snapshot = licenseService.Current;
        if (snapshot.Jti is null)
            return;

        // Offline-only keys are exempt from the server check: a forced refresh (admin "Re-check
        // now") just re-evaluates expiry locally — there is nothing to ask the server.
        if (snapshot.Offline)
        {
            EnforceOfflineExpiry(snapshot);
            return;
        }

        await checkLock.WaitAsync(cancellationToken);
        try
        {
            var result = await serverClient.CheckAsync(snapshot.Jti, appVersion.Version, cancellationToken);
            var next = Transition(licenseService.Current, result);
            licenseService.ApplySnapshot(next);

            if (result.Status == LicenseCheckResult.Valid)
            {
                cacheStore.Save(new LicenseCacheEntry(snapshot.Jti, result.CheckedAt, result.Status));
            }
        }
        finally
        {
            checkLock.Release();
        }
    }

    /// <summary>
    /// The license state machine: maps a server result against the current snapshot and the last
    /// successful contact (from the cache) into the next snapshot.
    /// </summary>
    private LicenseSnapshot Transition(LicenseSnapshot snapshot, LicenseCheckResult result)
    {
        switch (result.Status)
        {
            case LicenseCheckResult.Valid:
            {
                var tier = result.UpdatedTier ?? snapshot.Tier;
                var definition = LicensePolicy.For(tier);
                var limits = new Dictionary<LicenseLimit, long>(definition.Limits);
                foreach (var (k, v) in snapshot.Limits)
                    limits[k] = v;
                if (result.UpdatedLimits is not null)
                {
                    foreach (var (k, v) in result.UpdatedLimits)
                        limits[k] = v;
                }

                if (snapshot.Status != LicenseStatus.Active)
                    logger.LogInformation("License server confirmed valid; restoring Active tier {Tier}", tier);

                return snapshot with
                {
                    Tier = tier,
                    Status = LicenseStatus.Active,
                    GracePeriodEndsAt = null,
                    Features = result.UpdatedTier is null ? snapshot.Features : definition.Features,
                    Limits = limits,
                };
            }

            case LicenseCheckResult.Revoked:
            {
                logger.LogWarning("License {Jti} was revoked by the server; downgrading to Free", snapshot.Jti);
                return Expired(snapshot.Source);
            }

            default:
            {
                // Unknown / unreachable: fold into the offline grace window measured from the
                // last successful server contact. Use clock.UtcNow (not result.CheckedAt) so
                // that an Unknown response — where the server was never actually reached — still
                // advances the elapsed window correctly.
                return ApplyGrace(snapshot);
            }
        }
    }

    private LicenseSnapshot ApplyGrace(LicenseSnapshot snapshot)
    {
        var cached = cacheStore.Load();
        var now = clock.UtcNow;

        // The offline window has two stages, each OfflineGracePeriodDays long. Within the first
        // stage the deployment keeps running fully (Active). In the second stage it enters Grace
        // (warning surfaced to operators). Past both stages it degrades to Free.
        var stage = TimeSpan.FromDays(Math.Max(0, configuration.OfflineGracePeriodDays));
        var anchor = cached?.LastServerOkUtc ?? serviceStartedUtc;
        var elapsed = now - anchor;

        if (elapsed >= stage + stage)
        {
            logger.LogWarning(
                "License server unreachable for {Days:F1} days (grace {Grace} days x2); downgrading to Free",
                elapsed.TotalDays,
                configuration.OfflineGracePeriodDays);
            return Free(snapshot.Source);
        }

        if (elapsed >= stage)
        {
            if (snapshot.Status != LicenseStatus.Grace)
            {
                logger.LogWarning(
                    "License server unreachable; entering Grace until {Until}",
                    anchor + stage + stage);
            }

            return snapshot with
            {
                Status = LicenseStatus.Grace,
                GracePeriodEndsAt = anchor + stage + stage,
            };
        }

        // Still within the first stage: keep running at full tier.
        return snapshot with
        {
            Status = LicenseStatus.Active,
            GracePeriodEndsAt = null,
        };
    }

    /// <summary>
    /// Degrades an offline-only license to <see cref="LicenseStatus.Expired"/> (Free entitlements)
    /// once it passes its <c>exp</c>. With no server check, expiry is the only thing that ends an
    /// offline key, so it is enforced locally on every loop tick and forced refresh.
    /// </summary>
    private void EnforceOfflineExpiry(LicenseSnapshot snapshot)
    {
        if (snapshot.ExpiresAt is { } expiresAt && clock.UtcNow >= expiresAt)
        {
            logger.LogWarning(
                "Offline license {Jti} expired at {Expiry:o}; downgrading to Free",
                snapshot.Jti,
                expiresAt);
            licenseService.ApplySnapshot(Expired(snapshot.Source));
        }
    }

    /// <summary>
    /// The next wake for an offline-only license: the sooner of the regular poll interval and the
    /// moment its <c>exp</c> lands, so the license ends on time rather than up to one interval
    /// late. Never longer than <paramref name="period"/> (the normal poll interval), so the offline
    /// path adds no delay the online path didn't already use.
    /// </summary>
    private TimeSpan OfflineWakeDelay(LicenseSnapshot snapshot, TimeSpan period)
    {
        if (snapshot.ExpiresAt is not { } expiresAt)
            return period;

        var until = expiresAt - clock.UtcNow;
        return until > TimeSpan.Zero && until < period ? until : period;
    }

    private static LicenseSnapshot Free(LicenseSource source)
    {
        var snapshot = LicenseSnapshot.Free();
        return snapshot with { Status = LicenseStatus.Free, Source = source };
    }

    private static LicenseSnapshot Expired(LicenseSource source)
    {
        // Revocation collapses entitlements to Free, but the status records that the license was
        // explicitly invalidated (vs. never having had one). The source is kept so the UI can
        // still tell where the now-revoked license came from.
        var snapshot = LicenseSnapshot.Free();
        return snapshot with { Status = LicenseStatus.Expired, Source = source };
    }
}
