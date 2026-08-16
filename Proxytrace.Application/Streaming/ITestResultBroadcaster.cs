using System.Threading.Channels;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Streaming;

/// <summary>
/// Represents a evaluation event data.
/// </summary>
public record EvaluationEventData(
    Guid EvaluatorId,
    EvaluatorKind EvaluatorKind,
    string EvaluatorName,
    EvaluationScore? Score,
    string? Reasoning,
    string? ErrorMessage);

/// <summary>
/// Event raised when a test run occurs.
/// </summary>
public abstract record TestRunEvent(Guid RunId, Guid GroupId);

/// <summary>
/// Event raised when a test case started occurs.
/// </summary>
public record TestCaseStartedEvent(
    Guid RunId,
    Guid GroupId,
    Guid TestCaseId) : TestRunEvent(RunId, GroupId);

/// <summary>
/// Event raised when a inference done occurs.
/// </summary>
public record InferenceDoneEvent(
    Guid RunId,
    Guid GroupId,
    Guid TestCaseId) : TestRunEvent(RunId, GroupId);

/// <summary>
/// Event raised when a evaluation arrived occurs.
/// </summary>
public record EvaluationArrivedEvent(
    Guid RunId,
    Guid GroupId,
    Guid TestCaseId,
    EvaluationEventData Evaluation) : TestRunEvent(RunId, GroupId);

/// <summary>
/// Event raised when a test result arrived occurs.
/// </summary>
public record TestResultArrivedEvent(
    Guid RunId,
    Guid GroupId,
    Guid TestCaseId,
    EvaluationScore? OverallScore,
    IReadOnlyList<EvaluationEventData> Evaluations,
    long DurationMs,
    double? CostEur,
    long? TokensIn,
    long? TokensOut,
    long? CachedTokensIn) : TestRunEvent(RunId, GroupId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static TestResultArrivedEvent Create(ITestRun run, ITestResult result)
    {
        // Per-case cost/tokens ride along so the live run view can sum a running run's totals as each
        // case lands, instead of showing zeros until the terminal refetch. CalculateCost is linear, so
        // the client sum matches the backend's run-level TestRunTotals.
        var totals = TestRunTotals.FromResult(run, result);
        return new(
            run.Id,
            run.Group.Id,
            result.TestCase.Id,
            result.OverallScore,
            result.Evaluations.Select(e => new EvaluationEventData(
                e.Evaluator.Id,
                e.Evaluator.Kind,
                e.Evaluator.Name,
                e.Score,
                e.Reasoning,
                e.ErrorMessage)).ToArray(),
            (long)result.Latency.TotalMilliseconds,
            totals.CostEur is { } cost ? (double)cost : null,
            totals.TokensIn,
            totals.TokensOut,
            totals.CachedTokensIn);
    }
}

/// <summary>
/// Event raised when a run complete occurs.
/// </summary>
public record RunCompleteEvent(
    Guid RunId,
    Guid GroupId,
    TestRunStatus Status,
    DateTimeOffset? CompletedAt) : TestRunEvent(RunId, GroupId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static RunCompleteEvent Create(ITestRun testRun)
        => new(testRun.Id, testRun.Group.Id, testRun.Status, testRun.CompletedAt);
}

/// <summary>
/// Terminal event published to group-level subscribers when every run in the group has finished.
/// <see cref="TestRunEvent.RunId"/> is <see cref="Guid.Empty"/> for this event type.
/// </summary>
public record GroupRunCompleteEvent(
    Guid GroupId,
    TestRunStatus GroupStatus,
    DateTimeOffset? GroupCompletedAt) : TestRunEvent(Guid.Empty, GroupId)
{
    /// <summary>
    /// Creates.
    /// </summary>
    public static GroupRunCompleteEvent Create(ITestRunGroup group)
        => new(group.Id, group.Status, group.CompletedAt);
}

/// <summary>
/// Broadcasts test result events.
/// </summary>
public interface ITestResultBroadcaster
{
    /// <summary>Subscribe to real-time events for a single run.</summary>
    ChannelReader<TestRunEvent> Subscribe(Guid runId, CancellationToken cancellationToken);

    /// <summary>
    /// Subscribe to real-time events for all runs in a group, plus the final
    /// <see cref="GroupRunCompleteEvent"/> when every run has finished.
    /// </summary>
    ChannelReader<TestRunEvent> SubscribeToGroup(Guid groupId, CancellationToken cancellationToken);

    void Publish(TestRunEvent evt);
    void PublishComplete(RunCompleteEvent evt);
    void PublishGroupComplete(GroupRunCompleteEvent evt);
}
