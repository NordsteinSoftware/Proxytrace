using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Async;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// The authoritative <see cref="ILicenseService"/>. The constructor resolves the configured
/// license synchronously (override snapshot, environment JWT, or Free); an invalid configured
/// JWT degrades to Free-tier entitlements with <see cref="LicenseStatus.Invalid"/> instead of
/// failing the host. A license stored in the database is applied later, after migrations, via
/// <see cref="ILicenseActivator"/> and takes precedence.
/// </summary>
internal sealed class LicenseService : ILicenseService
{
    private static readonly Guid LockKey = Guid.NewGuid();

    private readonly IAsyncLock gate;
    private readonly Func<ILicenseRefreshTrigger> refreshTrigger;
    private readonly ILogger<LicenseService> logger;

    private volatile LicenseSnapshot current;

    public LicenseService(
        ConfiguredLicenseResolver resolver,
        IAsyncLock gate,
        Func<ILicenseRefreshTrigger> refreshTrigger,
        ILogger<LicenseService> logger)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.gate = gate;
        this.refreshTrigger = refreshTrigger;
        this.logger = logger;

        current = resolver.Resolve();
    }

    public LicenseSnapshot Current => current;

    public event Action? Changed;

    public bool IsFeatureEnabled(LicenseFeature feature) => current.Features.Contains(feature);

    public long GetLimit(LicenseLimit limit)
        => current.Limits.GetValueOrDefault(limit, 0);

    public Task ForceRefreshAsync(CancellationToken cancellationToken = default)
        => refreshTrigger().RunCheckNowAsync(cancellationToken);

    /// <summary>
    /// Replaces the current snapshot and raises <see cref="Changed"/> when it actually changed.
    /// Invoked by the background check service as the license state machine transitions.
    /// </summary>
    public void ApplySnapshot(LicenseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        using var _ = gate.Lock(LockKey);
        
        var changed = !SnapshotsEqual(current, snapshot);
        current = snapshot;

        if (changed)
        {
            logger.LogInformation(
                "License snapshot updated: tier {Tier}, status {Status}",
                snapshot.Tier,
                snapshot.Status);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Compares two snapshots structurally. The record's generated equality compares the Features
    /// set and Limits dictionary by reference, so a rebuilt-but-equivalent dictionary would falsely
    /// look "changed" on every successful poll; this compares those collections by value instead.
    /// </summary>
    private static bool SnapshotsEqual(LicenseSnapshot a, LicenseSnapshot b)
    {
        if (a.Tier != b.Tier
            || a.Status != b.Status
            || a.ExpiresAt != b.ExpiresAt
            || a.GracePeriodEndsAt != b.GracePeriodEndsAt
            || a.CustomerEmail != b.CustomerEmail
            || a.Jti != b.Jti
            || a.Source != b.Source
            || a.InvalidReason != b.InvalidReason
            || a.Offline != b.Offline)
        {
            return false;
        }

        return FeaturesEqual(a.Features, b.Features) && LimitsEqual(a.Limits, b.Limits);
    }

    private static bool FeaturesEqual(IReadOnlySet<LicenseFeature>? a, IReadOnlySet<LicenseFeature>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;

        return a.Count == b.Count && a.SetEquals(b);
    }

    private static bool LimitsEqual(
        IReadOnlyDictionary<LicenseLimit, long>? a,
        IReadOnlyDictionary<LicenseLimit, long>? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a.Count != b.Count)
            return false;

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value)
                return false;
        }

        return true;
    }
}