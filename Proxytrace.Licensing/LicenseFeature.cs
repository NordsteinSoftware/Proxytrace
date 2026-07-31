namespace Proxytrace.Licensing;

/// <summary>
/// A discrete product capability that may be gated behind a license tier.
/// </summary>
public enum LicenseFeature
{
    OptimizationProposals,
    AgenticEvaluators,
    CustomEvaluators,
    SsoOidc,
    AuditLog,
    Tracey,
    ScheduledTestRuns,
    CustomAnomalyDetectors,
    CostControls,
    TestCaseSynthesis,
}
