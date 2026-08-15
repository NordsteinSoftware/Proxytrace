using Core = Nordstein.Core.Licensing;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// Proxytrace's <see cref="ILicenseActivator"/>, backed by the Nordstein.Core licensing engine.
/// Converts snapshots at the boundary and rethrows engine rejections as
/// <see cref="Exceptions.InvalidLicenseException"/> so downstream catch sites are unaffected
/// by the extraction.
/// </summary>
internal sealed class LicenseActivatorAdapter : ILicenseActivator
{
    private readonly Core.ILicenseActivator engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseActivatorAdapter"/> class.
    /// </summary>
    public LicenseActivatorAdapter(Core.ILicenseActivator engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        this.engine = engine;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public LicenseSnapshot Validate(string licenseJwt)
        => Guarded(() => engine.Validate(licenseJwt));

    /// <summary>
    /// Activate.
    /// </summary>
    public LicenseSnapshot Activate(string licenseJwt, LicenseSource source)
        => Guarded(() => engine.Activate(licenseJwt, LicenseSnapshotMapper.ToCore(source)));

    /// <summary>
    /// Activate or invalid.
    /// </summary>
    public LicenseSnapshot ActivateOrInvalid(string licenseJwt, LicenseSource source)
        => LicenseSnapshotMapper.ToProduct(
            engine.ActivateOrInvalid(licenseJwt, LicenseSnapshotMapper.ToCore(source)));

    /// <summary>
    /// Activate configured.
    /// </summary>
    public LicenseSnapshot ActivateConfigured()
        => LicenseSnapshotMapper.ToProduct(engine.ActivateConfigured());

    private static LicenseSnapshot Guarded(Func<Core.LicenseSnapshot> operation)
    {
        try
        {
            return LicenseSnapshotMapper.ToProduct(operation());
        }
        catch (Core.InvalidLicenseException ex)
        {
            throw LicenseSnapshotMapper.ToProduct(ex);
        }
    }
}
