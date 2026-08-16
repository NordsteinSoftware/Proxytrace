using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.Statistics.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Application.Statistics.TestRun.Internal;

internal sealed class TestRunStatsProjector : AbstractStatsProjector<ITestRun, TestRunStats>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunStatsProjector"/> class.
    /// </summary>
    public TestRunStatsProjector(IStatsWriter<TestRunStats> writer, IRepository<ITestRun> repository) :
        base(writer, repository)
    {
    }

    /// <summary>
    /// Excludes internal system runs from the user-facing statistics.
    /// </summary>
    /// <remarks>
    /// A/B validation runs are executed by the optimizer, not by a user. <c>IsSystemRun</c> already
    /// hides them from the run list, so a user cannot see or inspect them — but their results were
    /// still folded into the pass-rate figures and the anomaly baseline, so people saw pass rates
    /// computed partly from runs that do not exist as far as the UI is concerned, moving for reasons
    /// they could not investigate. Being deliberately adversarial (a candidate prompt under test),
    /// those runs also skew the baseline that anomaly detection compares against.
    /// </remarks>
    protected override bool ShouldProject(ITestRun run) => !run.Group.IsSystemRun;

    protected override Task<TestRunStats> ComputeStatsAsync(ITestRun run, CancellationToken cancellationToken)
    {
        int testCases = run.Group.Suite.TestCases.Count;
        // Use the shared pass definition so the optimizer's stats-based vetting agrees with
        // the theory validator's gate and the proposal pass-rates shown in the UI.
        int passed = run.TestResults.Count(r => r.IsPass());

        TokenUsage? usage = run.TestResults
            .Select(r => r.Usage)
            .Aggregate<TokenUsage?, TokenUsage?>(null, (acc, next) => acc is null ? next : acc + next);

        TimeSpan? duration = run.TestResults.Count > 0
            ? TimeSpan.FromTicks(run.TestResults.Sum(r => r.Latency.Ticks))
            : null;

        decimal? cost = usage is not null
            ? run.Endpoint.CalculateCost(usage)
            : null;

        var stats = new TestRunStats(
            TestRunId: run.Id,
            AgentId: run.Group.Suite.Agent.Id,
            EndpointId: run.Endpoint.Id,
            GroupId: run.Group.Id,
            SuiteId: run.Group.Suite.Id,
            TestCases: testCases,
            Passed: passed,
            TotalDuration: duration,
            Usage: usage,
            Cost: cost,
            RunCompletedAt: run.CompletedAt ?? run.UpdatedAt);
        return Task.FromResult(stats);
    }
}
