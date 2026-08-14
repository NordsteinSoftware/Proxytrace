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

    public LicenseServiceAdapter(Core.ILicenseService engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        this.engine = engine;
    }

    public LicenseSnapshot Current => LicenseSnapshotMapper.ToProduct(engine.Current);

    public event Action? Changed
    {
        add => engine.Changed += value;
        remove => engine.Changed -= value;
    }

    public bool IsFeatureEnabled(LicenseFeature feature) => engine.HasFeature(feature.ToString());

    public long GetLimit(LicenseLimit limit) => engine.GetLimit(limit.ToString());

    public Task ForceRefreshAsync(CancellationToken cancellationToken = default)
        => engine.ForceRefreshAsync(cancellationToken);
}
