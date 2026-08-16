using Proxytrace.Licensing;

namespace Proxytrace.Api.Auth.Licensing;

/// <summary>
/// Marks a controller or action as requiring a specific licensed feature. The
/// <see cref="LicenseEnforcementFilter"/> rejects requests with HTTP 402 when the feature is not
/// granted by the current license.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequiresFeatureAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequiresFeatureAttribute"/> class.
    /// </summary>
    public RequiresFeatureAttribute(LicenseFeature feature)
    {
        Feature = feature;
    }

    /// <summary>
    /// The feature required to invoke the decorated endpoint.
    /// </summary>
    public LicenseFeature Feature { get; }
}
