namespace Proxytrace.Licensing;

/// <summary>
/// Static source of truth for the features and limits granted by each license tier.
/// Free is the fallback for any unknown or unrecognized tier.
/// </summary>
public static class LicensePolicy
{
    private static readonly TierDefinition FreeDefinition = new(
        new HashSet<LicenseFeature>(),
        new Dictionary<LicenseLimit, long>
        {
            [LicenseLimit.MaxProjects] = 1,
            // Single user on Free — invites are blocked once the seat is taken, effectively
            // disabling user management until an upgrade (OIDC/SSO is Enterprise-only anyway).
            [LicenseLimit.MaxUsers] = 1,
            [LicenseLimit.MaxAgents] = 1,
            [LicenseLimit.MaxTestSuites] = 1,
            [LicenseLimit.MaxTracesPerMonth] = 10_000,
            [LicenseLimit.TraceRetentionDays] = 14,
        });

    private static readonly TierDefinition EnterpriseDefinition = new(
        new HashSet<LicenseFeature>
        {
            LicenseFeature.OptimizationProposals,
            LicenseFeature.AgenticEvaluators,
            LicenseFeature.CustomEvaluators,
            LicenseFeature.SsoOidc,
            LicenseFeature.AuditLog,
            LicenseFeature.Tracey,
            LicenseFeature.ScheduledTestRuns,
            LicenseFeature.CustomAnomalyDetectors,
            LicenseFeature.CostControls,
            LicenseFeature.TestCaseSynthesis,
        },
        new Dictionary<LicenseLimit, long>
        {
            [LicenseLimit.MaxProjects] = long.MaxValue,
            [LicenseLimit.MaxUsers] = long.MaxValue,
            [LicenseLimit.MaxAgents] = long.MaxValue,
            [LicenseLimit.MaxTestSuites] = long.MaxValue,
            [LicenseLimit.MaxTracesPerMonth] = long.MaxValue,
            [LicenseLimit.TraceRetentionDays] = 365,
        });

    /// <summary>
    /// Returns the feature/limit definition for the given tier; Free for unknown tiers.
    /// </summary>
    public static TierDefinition For(LicenseTier tier) => tier switch
    {
        LicenseTier.Enterprise => EnterpriseDefinition,
        _ => FreeDefinition,
    };
}
