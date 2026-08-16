namespace Proxytrace.Storage.Internal.Entities.OutlierSettings;

/// <summary>The single-row outlier-detection sensitivity configuration.</summary>
internal record OutlierSettingsEntity : Entity
{
    /// <summary>
    /// Gets or sets the enabled.
    /// </summary>
    public required bool Enabled { get; init; }
    /// <summary>
    /// Gets or sets the sigma multiplier.
    /// </summary>
    public required double SigmaMultiplier { get; init; }
    /// <summary>
    /// Gets or sets the min sample count.
    /// </summary>
    public required int MinSampleCount { get; init; }
    /// <summary>
    /// Gets or sets the sample window.
    /// </summary>
    public required int SampleWindow { get; init; }
}
