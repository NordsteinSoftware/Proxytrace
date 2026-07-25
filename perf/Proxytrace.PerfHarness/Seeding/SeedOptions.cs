namespace Proxytrace.PerfHarness.Seeding;

/// <summary>Shape of the dataset the perf seeder builds.</summary>
internal sealed record SeedOptions(
    long TargetCalls = 1_000_000,
    int AgentCount = 50,
    int EndpointCount = 10,
    int DaysSpread = 90,
    double ErrorRate = 0.05,
    double ConversationRate = 0.30,
    // Fraction of calls stamped with a SessionId drawn from a pool of debugging sessions, so the
    // session-filtered traces list (agentCallsListBySession) and the recent-sessions list
    // (sessionsRecent) measure against realistic cardinality; a matching Sessions row is bulk-inserted
    // for every pool entry that received at least one call (see SeedAsync).
    double SessionRate = 0.30,
    // Fraction of calls stamped with a non-zero OutlierFlags bitmask so the partial outlier index
    // and the anomaly aggregates (anomalyTimeline) measure against a realistically small flagged
    // subset; a share of flagged rows also carries the CustomAnomaly bit (see BuildCall).
    double OutlierRate = 0.03,
    int BatchSize = 10_000,
    int RandomSeed = 1234,
    // Per-run test-run statistics rows (TestRunStatsEntity), spread across TestRunSuitePoolSize
    // synthetic suites, so the suite-scoped TestRunStats query (#253) can be measured at scale. One
    // TestRunStatsEntity needs one TestRunEntity (the TestRunId FK is 1:1), so this many test runs
    // are also seeded; SuiteId is a plain indexed column (no FK) so the suite spread is synthetic.
    long TestRunCount = 25_000,
    int TestRunSuitePoolSize = 250)
{
    public static SeedOptions ForSize(long targetCalls)
        => new()
        {
            TargetCalls = targetCalls,
            // Scale the TestRunStats dataset with the run size (capped) so a small smoke `--size`
            // stays quick while the full ~1M run seeds a meaningful scoped-query-at-scale dataset.
            TestRunCount = Math.Clamp(targetCalls / 40, 0L, 25_000L),
        };
}

/// <summary>What the seeder produced — captured so scenarios can target real ids without re-querying.</summary>
internal sealed record SeedSummary(
    Guid ProjectId,
    Guid ProviderId,
    IReadOnlyList<Guid> AgentIds,
    IReadOnlyList<Guid> EndpointIds,
    long CallsInserted,
    TimeSpan Elapsed);
