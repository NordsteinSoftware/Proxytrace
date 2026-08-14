using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Tests;

/// <summary>
/// <see cref="AgentCallSummary.Fold"/> and <see cref="AgentCallSummary.StdDev"/> are pure statics —
/// like <see cref="AgentCallHistogram"/>, they need no container, so every fake is built inside the
/// test method and nothing is shared.
/// </summary>
[TestClass]
public sealed class AgentCallSummaryTests
{
    private static readonly Guid EndpointA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EndpointB = new("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// A stub endpoint priced linearly per million tokens, mirroring the real
    /// <see cref="IModelEndpoint.CalculateCost"/> contract (cached is a subset of input, clamped).
    /// </summary>
    private static IModelEndpoint PricedEndpoint(decimal inputPerMillion, decimal outputPerMillion)
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.CalculateCost(Arg.Any<TokenUsage>()).Returns(call =>
        {
            var usage = call.Arg<TokenUsage>();
            ArgumentNullException.ThrowIfNull(usage);
            ulong cached = Math.Min(usage.CachedInputTokenCount, usage.InputTokenCount);
            ulong uncached = usage.InputTokenCount - cached;
            return (decimal?)((inputPerMillion * uncached
                + inputPerMillion * cached
                + outputPerMillion * usage.OutputTokenCount) / 1_000_000m);
        });
        return endpoint;
    }

    private static IModelEndpoint UnpricedEndpoint()
    {
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.CalculateCost(Arg.Any<TokenUsage>()).Returns((decimal?)null);
        return endpoint;
    }

    private static AgentCallSummaryGroup Group(
        Guid endpointId,
        int count = 1,
        ulong input = 0,
        ulong output = 0,
        ulong cached = 0,
        double latencySum = 0,
        double latencySumOfSquares = 0,
        int latencyCount = 0,
        int errorCount = 0)
        => new(endpointId, count, input, output, cached, latencySum, latencySumOfSquares, latencyCount, errorCount);

    [TestMethod]
    public void Fold_NoGroups_ReturnsEmpty()
    {
        var result = AgentCallSummary.Fold([], _ => null);

        result.Should().Be(AgentCallSummary.Empty);
        result.Count.Should().Be(0);
        result.TotalCost.Should().BeNull();
        result.AvgLatencyMs.Should().Be(0);
    }

    [TestMethod]
    public void Fold_SumsTokensAcrossGroups()
    {
        var groups = new[]
        {
            Group(EndpointA, count: 2, input: 100, output: 50, cached: 10),
            Group(EndpointB, count: 1, input: 7, output: 3, cached: 1),
        };

        var result = AgentCallSummary.Fold(groups, _ => null);

        result.Count.Should().Be(3);
        result.InputTokens.Should().Be(107);
        result.OutputTokens.Should().Be(53);
        result.CachedInputTokens.Should().Be(11);
    }

    [TestMethod]
    public void Fold_PricesEachGroupWithItsOwnEndpoint()
    {
        var cheap = PricedEndpoint(inputPerMillion: 1m, outputPerMillion: 2m);
        var pricey = PricedEndpoint(inputPerMillion: 10m, outputPerMillion: 20m);
        var groups = new[]
        {
            Group(EndpointA, count: 1, input: 1_000_000, output: 1_000_000),
            Group(EndpointB, count: 1, input: 1_000_000, output: 1_000_000),
        };

        var result = AgentCallSummary.Fold(
            groups,
            id => id == EndpointA ? cheap : pricey);

        // A: 1 + 2 = 3. B: 10 + 20 = 30. Collapsing both into one pricing call would give 6 or 60.
        result.TotalCost.Should().Be(33m);
    }

    [TestMethod]
    public void Fold_UnknownEndpoint_ContributesTokensButNoCost()
    {
        var known = PricedEndpoint(inputPerMillion: 1m, outputPerMillion: 1m);
        var groups = new[]
        {
            Group(EndpointA, count: 1, input: 1_000_000, output: 0),
            Group(EndpointB, count: 1, input: 1_000_000, output: 0),
        };

        var result = AgentCallSummary.Fold(
            groups,
            id => id == EndpointA ? known : null);

        result.InputTokens.Should().Be(2_000_000);
        result.TotalCost.Should().Be(1m);
    }

    [TestMethod]
    public void Fold_AllEndpointsUnpriced_TotalCostIsNullNotZero()
    {
        // "unknown price" and "free" are different facts and the UI renders them differently.
        var result = AgentCallSummary.Fold(
            [Group(EndpointA, count: 1, input: 500, output: 500)],
            _ => UnpricedEndpoint());

        result.TotalCost.Should().BeNull();
    }

    [TestMethod]
    public void Fold_AveragesLatencyOverNonNullLatencyCountOnly()
    {
        // 5 calls matched but only 3 recorded a latency — the mean is over the 3, not the 5.
        var result = AgentCallSummary.Fold(
            [Group(EndpointA, count: 5, latencySum: 300, latencySumOfSquares: 30_000, latencyCount: 3)],
            _ => null);

        result.Count.Should().Be(5);
        result.AvgLatencyMs.Should().Be(100);
    }

    [TestMethod]
    public void Fold_LatencyCountZero_AvgAndStdDevAreZero()
    {
        var result = AgentCallSummary.Fold(
            [Group(EndpointA, count: 4, latencySum: 0, latencySumOfSquares: 0, latencyCount: 0)],
            _ => null);

        result.AvgLatencyMs.Should().Be(0);
        result.LatencyStdDevMs.Should().Be(0);
    }

    [TestMethod]
    public void Fold_SumsErrorCounts()
    {
        var result = AgentCallSummary.Fold(
            [Group(EndpointA, count: 5, errorCount: 2), Group(EndpointB, count: 5, errorCount: 3)],
            _ => null);

        result.ErrorCount.Should().Be(5);
    }

    [TestMethod]
    public void StdDev_KnownDataset_MatchesPopulationStandardDeviation()
    {
        // 2,4,4,4,5,5,7,9 → sum 40, sum of squares 232, n 8, mean 5.
        // Population variance = (232 - 40²/8) / 8 = 32/8 = 4, so the deviation is 2.
        // (The sample deviation for the same data is sqrt(32/7) ≈ 2.138 — this aggregate covers the
        // whole matching set, so population is the right one.)
        AgentCallSummary.StdDev(sum: 40, sumOfSquares: 232, n: 8).Should().BeApproximately(2d, 1e-9);
    }

    [TestMethod]
    public void StdDev_SingleValue_IsZero()
    {
        // One value cannot deviate from itself; the KPI band shows "± 0".
        AgentCallSummary.StdDev(sum: 100, sumOfSquares: 10_000, n: 1).Should().Be(0);
    }

    [TestMethod]
    public void StdDev_IdenticalValues_IsZeroNotNaN()
    {
        // sumOfSquares - sum²/n can land marginally negative in floating point when every value
        // is identical; the result must clamp to 0 rather than surfacing NaN through sqrt.
        var result = AgentCallSummary.StdDev(sum: 1000, sumOfSquares: 100_000, n: 10);

        result.Should().Be(0);
        double.IsNaN(result).Should().BeFalse();
    }
}
