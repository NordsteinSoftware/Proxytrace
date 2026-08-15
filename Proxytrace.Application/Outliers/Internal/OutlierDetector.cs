using Proxytrace.Domain.Outliers;
using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Application.Outliers.Internal;

/// <summary>
/// Per-agent adaptive outlier detection. Compares a call's metrics to the agent's recent baseline
/// (mean ± N·stddev) and returns the flags that trip. High metrics (tokens, latency, tool calls) flag
/// above the upper bound; cache-hit rate flags below the lower bound. A metric is only evaluated when
/// its baseline has enough samples and a non-zero spread, so cold starts and perfectly-uniform agents
/// never produce noise.
/// </summary>
internal sealed class OutlierDetector : IOutlierDetector
{
    private readonly IOutlierSettingsStore settingsStore;
    private readonly IOutlierBaselineReader baselineReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutlierDetector"/> class.
    /// </summary>
    public OutlierDetector(IOutlierSettingsStore settingsStore, IOutlierBaselineReader baselineReader)
    {
        this.settingsStore = settingsStore;
        this.baselineReader = baselineReader;
    }

    /// <summary>
    /// Evaluates asynchronously.
    /// </summary>
    public async Task<OutlierFlags> EvaluateAsync(
        Guid agentId, OutlierMetrics metrics, CancellationToken cancellationToken = default)
    {
        OutlierSettings settings = await settingsStore.GetAsync(cancellationToken) ?? OutlierSettings.Default;
        if (!settings.Enabled)
        {
            return OutlierFlags.None;
        }

        OutlierBaseline baseline = await baselineReader.GetBaselineAsync(
            agentId, settings.SampleWindow, cancellationToken);

        var flags = OutlierFlags.None;
        if (IsHigh(metrics.TotalTokens, baseline.TotalTokens, settings))
        {
            flags |= OutlierFlags.HighTokens;
        }
        if (IsHigh(metrics.LatencyMs, baseline.LatencyMs, settings))
        {
            flags |= OutlierFlags.HighLatency;
        }
        if (metrics.CacheHitRate is { } cacheHitRate && IsLow(cacheHitRate, baseline.CacheHitRate, settings))
        {
            flags |= OutlierFlags.LowCacheHit;
        }
        if (IsHigh(metrics.ToolCalls, baseline.ToolCalls, settings))
        {
            flags |= OutlierFlags.ManyToolCalls;
        }
        return flags;
    }

    private static bool IsHigh(double value, MetricBaseline baseline, OutlierSettings settings)
        => Usable(baseline, settings) && value > baseline.Mean + settings.SigmaMultiplier * baseline.StdDev;

    private static bool IsLow(double value, MetricBaseline baseline, OutlierSettings settings)
        => Usable(baseline, settings) && value < baseline.Mean - settings.SigmaMultiplier * baseline.StdDev;

    // A metric needs enough samples and a non-zero spread to judge against: a zero stddev means every
    // recent call was identical, so any difference would otherwise trip — skip rather than flag noise.
    private static bool Usable(MetricBaseline baseline, OutlierSettings settings)
        => baseline.SampleCount >= settings.MinSampleCount && baseline.StdDev > 0d;
}
