using Core = Nordstein.Core.Licensing;

namespace Proxytrace.Licensing.Internal;

/// <summary>
/// Proxytrace's <see cref="ILicenseService"/>, backed by the Nordstein.Core licensing engine.
/// Feature/limit queries delegate directly on the canonical enum names; the snapshot is
/// converted on read so it always reflects the engine's current state.
/// </summary>
internal sealed class LicenseServiceAdapter : ILicenseService
{
    private readonly Core.ILicenseService engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="LicenseServiceAdapter"/> class.
    /// </summary>
    public LicenseServiceAdapter(Core.ILicenseService engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        this.engine = engine;
    }

    /// <summary>
    /// Gets the current.
    /// </summary>
    public LicenseSnapshot Current => LicenseSnapshotMapper.ToProduct(engine.Current);

    /// <summary>
    /// Occurs when changed.
    /// </summary>
    public event Action? Changed
    {
        add => engine.Changed += value;
        remove => engine.Changed -= value;
    }

    /// <summary>
    /// Determines whether the feature enabled.
    /// </summary>
    public bool IsFeatureEnabled(LicenseFeature feature) => engine.HasFeature(feature.ToString());

    /// <summary>
    /// Gets the limit.
    /// </summary>
    public long GetLimit(LicenseLimit limit) => engine.GetLimit(limit.ToString());

    /// <summary>
    /// Force refresh asynchronously.
    /// </summary>
    public Task ForceRefreshAsync(CancellationToken cancellationToken = default)
        => engine.ForceRefreshAsync(cancellationToken);
}
