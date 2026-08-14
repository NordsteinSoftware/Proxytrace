using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Statistics.TestRun;

/// <summary>
/// Per-run projection. One row per finalized <c>ITestRun</c>.
/// </summary>
public record TestRunStats(
    Guid TestRunId,
    Guid AgentId,
    Guid EndpointId,
    Guid GroupId,
    Guid SuiteId,
    int TestCases,
    int Passed,
    TimeSpan? TotalDuration,
    TokenUsage? Usage,
    decimal? Cost,
    DateTimeOffset RunCompletedAt)
{
    public int Failed => TestCases - Passed;

    public double? PassRate
        => TestCases > 0 ? Passed / (double)TestCases : null;
    
    public record Filter(
        Guid? AgentId = null,
        IReadOnlyCollection<Guid>? AgentIds = null,
        Guid? EndpointId = null,
        Guid? GroupId = null,
        Guid? SuiteId = null,
        IReadOnlyCollection<Guid>? SuiteIds = null,
        DateTimeOffset? From = null,
        DateTimeOffset? To = null);
}
