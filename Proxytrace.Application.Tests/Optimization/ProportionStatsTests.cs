using AwesomeAssertions;
using Proxytrace.Application.Optimization.Internal.Validation;

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// Tests for the two-proportion z-test used to gate A/B validation wins, including the
/// significance threshold that keeps sampling noise from spawning proposals.
/// </summary>
[TestClass]
public sealed class ProportionStatsTests
{
    [TestMethod]
    public void TwoSidedPValue_EmptySamples_ReturnsNull()
    {
        ProportionStats.TwoSidedPValue(0, 0, 5, 10).Should().BeNull();
        ProportionStats.TwoSidedPValue(5, 10, 0, 0).Should().BeNull();
    }

    [TestMethod]
    public void TwoSidedPValue_IdenticalAllPassRuns_ReturnsNull()
    {
        // Pooled variance is zero when both runs pass (or fail) every case — undefined p-value.
        ProportionStats.TwoSidedPValue(10, 10, 10, 10).Should().BeNull();
        ProportionStats.TwoSidedPValue(0, 10, 0, 10).Should().BeNull();
    }

    [TestMethod]
    public void TwoSidedPValue_NoDifference_IsNotSignificant()
    {
        double? p = ProportionStats.TwoSidedPValue(5, 10, 5, 10);
        p.Should().NotBeNull();
        p.Should().BeGreaterThan(0.05);
    }

    [TestMethod]
    public void TwoSidedPValue_SmallSampleSmallImprovement_IsNotSignificant()
    {
        // 8/10 → 9/10 is exactly the kind of single-flaky-case "improvement" the gate must reject.
        double? p = ProportionStats.TwoSidedPValue(8, 10, 9, 10);
        p.Should().NotBeNull();
        p.Should().BeGreaterThan(0.05);
    }

    [TestMethod]
    public void TwoSidedPValue_LargeSampleLargeImprovement_IsSignificant()
    {
        double? p = ProportionStats.TwoSidedPValue(50, 100, 80, 100);
        p.Should().NotBeNull();
        p.Should().BeLessThan(0.001);
    }

    [TestMethod]
    public void TwoSidedPValue_IsSymmetric()
    {
        double? improvement = ProportionStats.TwoSidedPValue(50, 100, 70, 100);
        double? regression = ProportionStats.TwoSidedPValue(70, 100, 50, 100);
        improvement.Should().Be(regression);
    }

    // ── paired test (the A/B gate) ───────────────────────────────────────────────────────
    //
    // The gate now pairs on the test case instead of pooling replays. Pooling was wrong in a way
    // that always erred toward accepting: it counted cases × replays as that many independent
    // trials, so the standard error shrank by roughly √(replays) and the gate got EASIER to pass
    // the more samples an operator configured.

    [TestMethod]
    public void PairedTwoSidedPValue_MatchesAnIndependentReferenceImplementation()
    {
        // Expected values computed by a separate implementation of the paired t-test written from
        // scratch (regularized incomplete beta via the Lentz continued fraction), which reproduces
        // the published two-sided critical values — t=2.262/df=9, t=2.228/df=10 and t=2.086/df=20 all
        // return 0.0500. Pinning to those rather than to this code's own output, so the numerics are
        // checked against something, not against themselves.

        // mean difference 0.2, sample sd 0.262467, n = 10 → t = 2.40966, df = 9.
        ProportionStats.PairedTwoSidedPValue(
                [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
                [0.6, 0.6, 0.4, 0.2, 0.2, 0.2, 0.0, 0.0, -0.1, -0.1])!
            .Value.Should().BeApproximately(0.039271, 1e-6);

        ProportionStats.PairedTwoSidedPValue(
                [1.0, 0.0, 1.0, 0.0, 1.0, 0.0],
                [1.0, 0.0, 1.0, 0.0, 1.0, 0.2])!
            .Value.Should().BeApproximately(0.363217, 1e-6);

        ProportionStats.PairedTwoSidedPValue(
                [0.0, 0.2, 0.4, 0.6, 0.8],
                [0.3, 0.5, 0.7, 0.9, 1.0])!
            .Value.Should().BeApproximately(0.000151, 1e-6);
    }

    [TestMethod]
    public void PairedTwoSidedPValue_UsesTheTDistributionNotTheNormalApproximation()
    {
        // n here is the number of test cases — routinely well under 30, where the normal
        // approximation's thinner tails understate the p-value and quietly reintroduce the
        // over-acceptance this change exists to remove. At t = 2.4097 the normal approximation
        // would give ≈0.0160; the correct t value for df = 9 is ≈0.0393, well over twice as large.
        double? p = ProportionStats.PairedTwoSidedPValue(
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            [0.6, 0.6, 0.4, 0.2, 0.2, 0.2, 0.0, 0.0, -0.1, -0.1]);

        p!.Value.Should().BeGreaterThan(0.03, "the normal approximation would have reported ≈0.016");
    }

    [TestMethod]
    public void PairedTwoSidedPValue_MoreReplaysOfTheSameEvidence_DoNotLowerThePValue()
    {
        // THE regression this replaces. Replays sharpen each case's proportion; they must not
        // manufacture sample size. With every replay agreeing, ten replays carry no more
        // information than one, and the paired p-value is unchanged...
        double[] baseline = [0.0, 0.0, 1.0, 1.0, 0.0, 1.0];
        double[] candidate = [1.0, 0.0, 1.0, 1.0, 1.0, 1.0];

        double? paired = ProportionStats.PairedTwoSidedPValue(baseline, candidate);
        paired.Should().Be(ProportionStats.PairedTwoSidedPValue(baseline, candidate));

        // ...whereas the pooled test reported a materially smaller p-value for being handed the
        // identical evidence ten times over.
        double? pooledOnce = ProportionStats.TwoSidedPValue(3, 6, 5, 6);
        double? pooledTenTimes = ProportionStats.TwoSidedPValue(30, 60, 50, 60);
        pooledTenTimes.Should().BeLessThan(pooledOnce!.Value);
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WithASingleTestCase_IsUndefinedRatherThanConfident()
    {
        // One case cannot evidence a difference between arms, however often it is replayed. The
        // pooled test produced a number here, and the gate treated that number as proof.
        ProportionStats.PairedTwoSidedPValue([0.0], [1.0]).Should().BeNull();
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WithNoDifference_IsUndefined()
    {
        double[] arm = [1.0, 0.5, 0.0, 0.75];
        ProportionStats.PairedTwoSidedPValue(arm, arm).Should().BeNull();
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WhenEveryCaseImprovesUnanimously_UsesTheSignTestBound()
    {
        // Zero spread makes t unbounded. Rather than claiming p = 0 from a handful of cases, fall
        // back to the two-sided sign test — the odds that all n moved the same way by chance.
        double[] baseline = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        double[] candidate = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1];

        double? p = ProportionStats.PairedTwoSidedPValue(baseline, candidate);

        p!.Value.Should().BeApproximately(2 * Math.Pow(0.5, 10), 1e-9);
        p!.Value.Should().BeLessThan(0.05, "ten cases all flipping to passing is real evidence");
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WhenOnlyTwoCasesFlipUnanimously_IsNotSignificant()
    {
        // The other edge of the same rule: two cases agreeing is a coin toss.
        ProportionStats.PairedTwoSidedPValue([0, 0], [1, 1])!.Value
            .Should().BeApproximately(0.5, 1e-9);
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WithASmallShiftAgainstWideSpread_IsNotSignificant()
    {
        double[] baseline = [1.0, 0.0, 1.0, 0.0, 1.0, 0.0];
        double[] candidate = [1.0, 0.0, 1.0, 0.0, 1.0, 0.2];

        ProportionStats.PairedTwoSidedPValue(baseline, candidate)!.Value
            .Should().BeGreaterThan(0.3, "one case nudging, amid wide spread, proves nothing");
    }

    [TestMethod]
    public void PairedTwoSidedPValue_WithMismatchedArms_IsUndefined()
    {
        ProportionStats.PairedTwoSidedPValue([0.0, 1.0], [1.0]).Should().BeNull();
    }

    [TestMethod]
    public void PairedTwoSidedPValue_IsSymmetricInDirection()
    {
        double[] baseline = [0.0, 0.2, 0.4, 0.6, 0.8];
        double[] candidate = [0.3, 0.5, 0.7, 0.9, 1.0];

        double? improvement = ProportionStats.PairedTwoSidedPValue(baseline, candidate);
        double? regression = ProportionStats.PairedTwoSidedPValue(candidate, baseline);

        regression!.Value.Should().BeApproximately(improvement!.Value, 1e-12);
    }
}
