using Proxytrace.Licensing.Exceptions;
using Core = Nordstein.Core.Licensing;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// Maps between the Nordstein.Core licensing engine's string-keyed snapshot and Proxytrace's
/// enum-typed one. The engine resolves every name through
/// <see cref="ProxytraceLicenseTierPolicy"/> before it reaches a snapshot, so the parses here
/// always succeed; unknown names are skipped defensively rather than thrown on.
/// </summary>
internal static class LicenseSnapshotMapper
{
    /// <summary>
    /// To product.
    /// </summary>
    public static LicenseSnapshot ToProduct(Core.LicenseSnapshot snapshot)
    {
        var tier = Enum.TryParse<LicenseTier>(snapshot.Tier, ignoreCase: true, out var parsedTier)
            ? parsedTier
            : LicenseTier.Free;

        var features = new HashSet<LicenseFeature>();
        foreach (var feature in snapshot.Features)
        {
            if (Enum.TryParse<LicenseFeature>(feature, ignoreCase: true, out var parsed))
                features.Add(parsed);
        }

        var limits = new Dictionary<LicenseLimit, long>();
        foreach (var (name, value) in snapshot.Limits)
        {
            if (Enum.TryParse<LicenseLimit>(name, ignoreCase: true, out var parsed))
                limits[parsed] = value;
        }

        return new LicenseSnapshot(
            tier,
            ToProduct(snapshot.Status),
            snapshot.ExpiresAt,
            snapshot.GracePeriodEndsAt,
            snapshot.CustomerEmail,
            snapshot.Jti,
            features,
            limits,
            ToProduct(snapshot.Source),
            snapshot.InvalidReason,
            snapshot.Offline);
    }

    /// <summary>
    /// To core.
    /// </summary>
    public static Core.LicenseSnapshot ToCore(LicenseSnapshot snapshot)
        => new(
            snapshot.Tier.ToString(),
            ToCore(snapshot.Status),
            snapshot.ExpiresAt,
            snapshot.GracePeriodEndsAt,
            snapshot.CustomerEmail,
            snapshot.Jti,
            snapshot.Features.Select(f => f.ToString()).ToHashSet(),
            snapshot.Limits.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            ToCore(snapshot.Source),
            snapshot.InvalidReason,
            snapshot.Offline);

    /// <summary>
    /// To product.
    /// </summary>
    public static LicenseStatus ToProduct(Core.LicenseStatus status) => status switch
    {
        Core.LicenseStatus.Free => LicenseStatus.Free,
        Core.LicenseStatus.Active => LicenseStatus.Active,
        Core.LicenseStatus.Grace => LicenseStatus.Grace,
        Core.LicenseStatus.Expired => LicenseStatus.Expired,
        Core.LicenseStatus.Invalid => LicenseStatus.Invalid,
        _ => LicenseStatus.Free,
    };

    /// <summary>
    /// To core.
    /// </summary>
    public static Core.LicenseStatus ToCore(LicenseStatus status) => status switch
    {
        LicenseStatus.Free => Core.LicenseStatus.Free,
        LicenseStatus.Active => Core.LicenseStatus.Active,
        LicenseStatus.Grace => Core.LicenseStatus.Grace,
        LicenseStatus.Expired => Core.LicenseStatus.Expired,
        LicenseStatus.Invalid => Core.LicenseStatus.Invalid,
        _ => Core.LicenseStatus.Free,
    };

    /// <summary>
    /// To product.
    /// </summary>
    public static LicenseSource ToProduct(Core.LicenseSource source) => source switch
    {
        Core.LicenseSource.None => LicenseSource.None,
        Core.LicenseSource.Environment => LicenseSource.Environment,
        Core.LicenseSource.Stored => LicenseSource.Stored,
        Core.LicenseSource.Override => LicenseSource.Override,
        _ => LicenseSource.None,
    };

    /// <summary>
    /// To core.
    /// </summary>
    public static Core.LicenseSource ToCore(LicenseSource source) => source switch
    {
        LicenseSource.None => Core.LicenseSource.None,
        LicenseSource.Environment => Core.LicenseSource.Environment,
        LicenseSource.Stored => Core.LicenseSource.Stored,
        LicenseSource.Override => Core.LicenseSource.Override,
        _ => Core.LicenseSource.None,
    };

    /// <summary>
    /// Rethrows the engine's rejection as Proxytrace's own exception type, preserving the
    /// reason, message, and cause, so downstream catch sites keep working unchanged.
    /// </summary>
    public static InvalidLicenseException ToProduct(Core.InvalidLicenseException exception)
        => new(ToProduct(exception.Reason), exception.Message, exception);

    /// <summary>
    /// To product.
    /// </summary>
    public static InvalidLicenseReason ToProduct(Core.InvalidLicenseReason reason) => reason switch
    {
        Core.InvalidLicenseReason.Malformed => InvalidLicenseReason.Malformed,
        Core.InvalidLicenseReason.BadSignature => InvalidLicenseReason.BadSignature,
        Core.InvalidLicenseReason.WrongIssuer => InvalidLicenseReason.WrongIssuer,
        Core.InvalidLicenseReason.WrongAudience => InvalidLicenseReason.WrongAudience,
        Core.InvalidLicenseReason.Expired => InvalidLicenseReason.Expired,
        Core.InvalidLicenseReason.MissingClaim => InvalidLicenseReason.MissingClaim,
        _ => InvalidLicenseReason.Malformed,
    };
}
