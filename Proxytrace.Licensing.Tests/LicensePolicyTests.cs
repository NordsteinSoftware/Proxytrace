using AwesomeAssertions;

namespace Proxytrace.Licensing.Tests;

[TestClass]
public sealed class LicensePolicyTests
{
    public required TestContext TestContext { get; init; }

    [TestMethod]
    public void For_Free_HasExpectedLimits()
    {
        var definition = LicensePolicy.For(LicenseTier.Free);

        definition.Features.Should().BeEmpty();
        definition.Features.Should().NotContain(LicenseFeature.ScheduledTestRuns);
        definition.Limits[LicenseLimit.MaxProjects].Should().Be(1);
        definition.Limits[LicenseLimit.MaxUsers].Should().Be(1);
        definition.Limits[LicenseLimit.MaxAgents].Should().Be(1);
        definition.Limits[LicenseLimit.MaxTestSuites].Should().Be(1);
        definition.Limits[LicenseLimit.MaxTracesPerMonth].Should().Be(10_000);
        definition.Limits[LicenseLimit.TraceRetentionDays].Should().Be(14);
    }

    [TestMethod]
    public void For_Enterprise_GrantsAllFeaturesAndUnlimitedCounts()
    {
        var definition = LicensePolicy.For(LicenseTier.Enterprise);

        definition.Features.Should().Contain(LicenseFeature.OptimizationProposals);
        definition.Features.Should().Contain(LicenseFeature.AgenticEvaluators);
        definition.Features.Should().Contain(LicenseFeature.CustomEvaluators);
        definition.Features.Should().Contain(LicenseFeature.SsoOidc);
        definition.Features.Should().Contain(LicenseFeature.AuditLog);
        definition.Features.Should().Contain(LicenseFeature.Tracey);
        definition.Features.Should().Contain(LicenseFeature.ScheduledTestRuns);
        definition.Features.Should().Contain(LicenseFeature.CustomAnomalyDetectors);
        definition.Features.Should().Contain(LicenseFeature.CostControls);
        definition.Features.Should().Contain(LicenseFeature.TestCaseSynthesis);
        definition.Limits[LicenseLimit.MaxProjects].Should().Be(long.MaxValue);
        definition.Limits[LicenseLimit.MaxUsers].Should().Be(long.MaxValue);
        definition.Limits[LicenseLimit.MaxAgents].Should().Be(long.MaxValue);
        definition.Limits[LicenseLimit.MaxTestSuites].Should().Be(long.MaxValue);
        definition.Limits[LicenseLimit.MaxTracesPerMonth].Should().Be(long.MaxValue);
        definition.Limits[LicenseLimit.TraceRetentionDays].Should().Be(365);
    }

    [TestMethod]
    public void For_UnknownTier_FallsBackToFree()
    {
        var definition = LicensePolicy.For((LicenseTier)999);

        definition.Limits[LicenseLimit.MaxProjects].Should().Be(1);
    }
}
