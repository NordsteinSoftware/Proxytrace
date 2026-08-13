using Proxytrace.Domain.Outliers;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Outliers;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class OutlierDetectorTests : BaseTest<Module>
{
    private static readonly OutlierSettings Defaults = new(Enabled: true, SigmaMultiplier: 3.0, MinSampleCount: 5, SampleWindow: 200);

    [TestMethod]
    public async Task Evaluate_TokensAboveUpperBound_FlagsHighTokens()
    {
        // mean 100 + 3·10 = 130; 200 is above.
        var flags = await Evaluate(Defaults, Baseline(tokens: new(100, 10, 50)), Metrics(tokens: 200));

        flags.Should().Be(OutlierFlags.HighTokens);
    }

    [TestMethod]
    public async Task Evaluate_TokensBelowUpperBound_NotFlagged()
    {
        var flags = await Evaluate(Defaults, Baseline(tokens: new(100, 10, 50)), Metrics(tokens: 125));

        flags.Should().Be(OutlierFlags.None);
    }

    [TestMethod]
    public async Task Evaluate_FewerSamplesThanMinimum_NotFlagged()
    {
        // Far above the bound, but only 3 samples (< MinSampleCount 5) → cold start, no flag.
        var flags = await Evaluate(Defaults, Baseline(tokens: new(100, 10, 3)), Metrics(tokens: 9999));

        flags.Should().Be(OutlierFlags.None);
    }

    [TestMethod]
    public async Task Evaluate_ZeroStdDev_NotFlagged()
    {
        // Every recent call identical (stddev 0) → any difference would otherwise trip; skip instead.
        var flags = await Evaluate(Defaults, Baseline(tokens: new(100, 0, 50)), Metrics(tokens: 9999));

        flags.Should().Be(OutlierFlags.None);
    }

    [TestMethod]
    public async Task Evaluate_WhenDisabled_ReturnsNone()
    {
        var disabled = Defaults with { Enabled = false };

        var flags = await Evaluate(disabled, Baseline(tokens: new(100, 10, 50)), Metrics(tokens: 9999));

        flags.Should().Be(OutlierFlags.None);
    }

    [TestMethod]
    public async Task Evaluate_CacheHitBelowLowerBound_FlagsLowCacheHit()
    {
        // mean 0.9 − 3·0.05 = 0.75; 0.5 is below.
        var flags = await Evaluate(Defaults, Baseline(cache: new(0.9, 0.05, 50)), Metrics(cache: 0.5));

        flags.Should().Be(OutlierFlags.LowCacheHit);
    }

    [TestMethod]
    public async Task Evaluate_CacheHitNull_NeverFlagsLowCacheHit()
    {
        // Turn-1 calls carry no cache-hit sample, so a low baseline must not flag them.
        var flags = await Evaluate(Defaults, Baseline(cache: new(0.9, 0.05, 50)), Metrics(cache: null));

        flags.Should().Be(OutlierFlags.None);
    }

    [TestMethod]
    public async Task Evaluate_MultipleMetricsTrip_CombinesFlags()
    {
        var baseline = Baseline(tokens: new(100, 10, 50), tools: new(2, 1, 50));

        var flags = await Evaluate(Defaults, baseline, Metrics(tokens: 200, tools: 10));

        flags.Should().Be(OutlierFlags.HighTokens | OutlierFlags.ManyToolCalls);
    }

    [TestMethod]
    public async Task Evaluate_LatencyAboveUpperBound_FlagsHighLatency()
    {
        var flags = await Evaluate(Defaults, Baseline(latency: new(500, 50, 50)), Metrics(latency: 1000));

        flags.Should().Be(OutlierFlags.HighLatency);
    }

    private async Task<OutlierFlags> Evaluate(OutlierSettings settings, OutlierBaseline baseline, OutlierMetrics metrics)
    {
        var settingsStore = Substitute.For<IOutlierSettingsStore>();
        settingsStore.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<OutlierSettings?>(settings));
        var baselineReader = Substitute.For<IOutlierBaselineReader>();
        baselineReader.GetBaselineAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(baseline));

        IServiceProvider services = GetServices(builder =>
        {
            builder.RegisterInstance(settingsStore).As<IOutlierSettingsStore>();
            builder.RegisterInstance(baselineReader).As<IOutlierBaselineReader>();
        });

        var detector = services.GetRequiredService<IOutlierDetector>();
        return await detector.EvaluateAsync(Guid.NewGuid(), metrics, CancellationToken);
    }

    private static OutlierBaseline Baseline(
        MetricBaseline tokens = default,
        MetricBaseline latency = default,
        MetricBaseline cache = default,
        MetricBaseline tools = default)
        => new(tokens, latency, cache, tools);

    private static OutlierMetrics Metrics(double tokens = 0, double latency = 0, double? cache = null, int tools = 0)
        => new(tokens, latency, cache, tools);
}
