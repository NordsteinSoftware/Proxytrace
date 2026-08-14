using Core = Nordstein.Core.Licensing;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// Supplies Proxytrace's licensing vocabulary to the Nordstein.Core licensing engine. The
/// canonical names are the enum member names of <see cref="LicenseTier"/>,
/// <see cref="LicenseFeature"/>, and <see cref="LicenseLimit"/> — which are also the JWT claim
/// values the license server signs, so they are a wire-format contract and must not change.
/// </summary>
internal sealed class ProxytraceLicenseTierPolicy : Core.ILicenseTierPolicy
{
    public string FallbackTier => nameof(LicenseTier.Free);

    public Core.TierDefinition GetDefinition(string tier)
    {
        var resolved = Enum.TryParse<LicenseTier>(tier, ignoreCase: true, out var parsed)
            ? parsed
            : LicenseTier.Free;
        var definition = LicensePolicy.For(resolved);

        return new Core.TierDefinition(
            definition.Features.Select(f => f.ToString()).ToHashSet(),
            definition.Limits.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value));
    }

    public bool TryResolveTier(string? value, out string tier)
        => TryResolve<LicenseTier>(value, out tier);

    public bool TryResolveFeature(string value, out string feature)
        => TryResolve<LicenseFeature>(value, out feature);

    public bool TryResolveLimit(string value, out string limit)
        => TryResolve<LicenseLimit>(value, out limit);

    private static bool TryResolve<TEnum>(string? value, out string resolved)
        where TEnum : struct, Enum
    {
        // Numeric strings parse as undefined enum values, so require a defined member — the
        // canonical spelling is the member name, matched case-insensitively like the previous
        // enum-based claim parsing did.
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            resolved = parsed.ToString();
            return true;
        }

        resolved = string.Empty;
        return false;
    }
}
