using AwesomeAssertions;
using Proxytrace.Domain.Statistics;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class StatisticsTimeTests
{
    [TestMethod]
    public void BucketStart_WithNonUtcOffset_AlignsToUtcBoundaries()
    {
        // 10:20 at +05:30 is 04:50 UTC. Bucketing must align on UTC boundaries regardless of the
        // input offset, matching the epoch-division grouping (WidthMilliseconds/BucketStartFromIndex).
        var timestamp = new DateTimeOffset(2026, 1, 1, 10, 20, 0, TimeSpan.FromMinutes(330));

        StatisticsBucket.FiveMinutes.BucketStart(timestamp)
            .Should().Be(new DateTimeOffset(2026, 1, 1, 4, 50, 0, TimeSpan.Zero));
        StatisticsBucket.Hourly.BucketStart(timestamp)
            .Should().Be(new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero));
        StatisticsBucket.Daily.BucketStart(timestamp)
            .Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [TestMethod]
    public void BucketStart_AgreesWithEpochIndexBucketing()
    {
        // The documented contract: floor((t - epoch) / width) → BucketStartFromIndex must land on
        // the same instant as BucketStart, for any input offset.
        var timestamp = new DateTimeOffset(2026, 6, 15, 23, 47, 12, TimeSpan.FromHours(-7));

        foreach (StatisticsBucket bucket in Enum.GetValues<StatisticsBucket>())
        {
            long index = (long)Math.Floor(
                (timestamp - DateTimeOffset.UnixEpoch).TotalMilliseconds / bucket.WidthMilliseconds());

            bucket.BucketStartFromIndex(index).Should().Be(
                bucket.BucketStart(timestamp),
                $"bucket {bucket} must agree between the two bucketing paths");
        }
    }

    [TestMethod]
    public void BucketCount_CountsTheInclusiveGridTheClientDraws()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        // Both ends inclusive: a window covering exactly two day boundaries is two buckets.
        StatisticsBucket.Daily.BucketCount(from, from.AddDays(1)).Should().Be(2);
        StatisticsBucket.Hourly.BucketCount(from, from.AddMinutes(59)).Should().Be(1);
        // Partial buckets at either end still count — the axis renders them.
        StatisticsBucket.Hourly.BucketCount(from.AddMinutes(30), from.AddMinutes(90)).Should().Be(2);
    }

    [TestMethod]
    public void CoarsenToFit_WhenTheRequestFits_KeepsIt()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        // 12 hours is 144 five-minute buckets — an explicit choice that fits is never overridden.
        StatisticsBucket.FiveMinutes.CoarsenToFit(from, from.AddHours(12), 400)
            .Should().Be(StatisticsBucket.FiveMinutes);
    }

    [TestMethod]
    public void CoarsenToFit_NeverRefinesAWideBucketThatAlreadyFits()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        // Unlike ForWindow this only ever coarsens: a daily request over an hour stays daily.
        StatisticsBucket.Daily.CoarsenToFit(from, from.AddHours(1), 400)
            .Should().Be(StatisticsBucket.Daily);
    }

    [TestMethod]
    public void CoarsenToFit_WalksToTheFinestGranularityThatFits()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        // A month at 5 minutes is 8,640 cells to draw 400 bars; hourly (745) still overflows, so
        // the answer is daily — the finest that fits, not simply the coarsest available.
        StatisticsBucket.FiveMinutes.CoarsenToFit(from, from.AddDays(31), 400)
            .Should().Be(StatisticsBucket.Daily);

        // Ten days: 5-minute overflows, hourly (241) fits — so it stops there rather than at daily.
        StatisticsBucket.FiveMinutes.CoarsenToFit(from, from.AddDays(10), 400)
            .Should().Be(StatisticsBucket.Hourly);
    }

    [TestMethod]
    public void CoarsenToFit_WhenEvenDailyOverflows_ReturnsTheCoarsestAvailable()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // Nothing finer exists to fall back to; the client truncates the tail as it always has.
        StatisticsBucket.FiveMinutes.CoarsenToFit(from, from.AddYears(5), 400)
            .Should().Be(StatisticsBucket.Daily);
    }

    [TestMethod]
    public void CoarsenToFit_WithADegenerateWindow_ReturnsTheRequest()
    {
        var from = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        StatisticsBucket.Hourly.CoarsenToFit(from, from, 400).Should().Be(StatisticsBucket.Hourly);
        StatisticsBucket.Hourly.CoarsenToFit(from, from.AddDays(1), 0).Should().Be(StatisticsBucket.Hourly);
    }
}
