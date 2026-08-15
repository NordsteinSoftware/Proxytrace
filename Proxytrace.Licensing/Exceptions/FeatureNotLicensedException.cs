namespace Proxytrace.Licensing.Exceptions;

/// <summary>
/// Thrown when an operation requires a feature not granted by the current license tier.
/// Surfaced to clients as HTTP 402 Payment Required.
/// </summary>
public sealed class FeatureNotLicensedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FeatureNotLicensedException"/> class.
    /// </summary>
    public FeatureNotLicensedException(LicenseFeature feature, LicenseTier tier)
        : base($"The feature '{feature}' is not available on the '{tier}' tier.")
    {
        Feature = feature;
        Tier = tier;
    }

    /// <summary>
    /// The feature that was requested but is not licensed.
    /// </summary>
    public LicenseFeature Feature { get; }

    /// <summary>
    /// The current license tier.
    /// </summary>
    public LicenseTier Tier { get; }
}
