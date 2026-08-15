namespace Proxytrace.Api.Dto.OutlierSettings;

/// <summary>
/// Maps outlier settings dto between representations.
/// </summary>
public sealed class OutlierSettingsDtoMapper
{
    /// <summary>
    /// To dto.
    /// </summary>
    public OutlierSettingsDto ToDto(Domain.Outliers.OutlierSettings s) => new(
        s.Enabled, s.SigmaMultiplier, s.MinSampleCount, s.SampleWindow);
}
