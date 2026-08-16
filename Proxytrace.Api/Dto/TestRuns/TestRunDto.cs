using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Api.Dto.TestRuns;

/// <summary>
/// Data transfer object representing a run evaluator.
/// </summary>
public record RunEvaluatorDto(Guid Id, EvaluatorKind Kind, string Name);

/// <summary>
/// Data transfer object representing a evaluation result.
/// </summary>
public record EvaluationResultDto(
    Guid EvaluatorId,
    EvaluatorKind EvaluatorKind,
    string EvaluatorName,
    EvaluationScore? Score,
    string? Reasoning,
    string? ErrorMessage);

/// <summary>
/// Data transfer object representing a test run.
/// </summary>
public record TestRunDto(
    Guid Id,
    Guid GroupId,
    Guid SuiteId,
    string SuiteName,
    Guid AgentId,
    string AgentName,
    Guid EndpointId,
    string EndpointName,
    int SampleIndex,
    TestRunStatus Status,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double PassRate,
    double? CostEur,
    long? TokensIn,
    long? TokensOut,
    long? CachedTokensIn,
    IReadOnlyList<RunEvaluatorDto> Evaluators,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    // Average per-case model inference latency over the run's results (aggregated inference latency,
    // NOT a wall-clock CompletedAt - StartedAt timer). Null until the run has at least one result.
    long? DurationMs,
    IReadOnlyList<TestCaseRowDto> TestCases,
    IReadOnlyList<TestResultDto> Results,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Data transfer object representing a test case row.
/// </summary>
public record TestCaseRowDto(Guid Id, string Summary);

/// <summary>
/// Data transfer object representing a test result.
/// </summary>
public record TestResultDto(
    Guid Id,
    Guid TestCaseId,
    string TestCaseSummary,
    string ActualResponse,
    IReadOnlyList<EvaluationResultDto> Evaluations,
    long DurationMs,
    double? CostEur,
    long? TokensIn,
    long? TokensOut,
    long? CachedTokensIn);

/// <summary>
/// Data transfer object representing a test run message.
/// </summary>
public record TestRunMessageDto(string Role, string Content);

/// <summary>
/// Data transfer object representing a test run group.
/// </summary>
public record TestRunGroupDto(
    Guid Id,
    Guid SuiteId,
    string SuiteName,
    Guid AgentId,
    string AgentName,
    TestRunStatus Status,
    bool IsSystemRun,
    int SampleCount,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TestRunDto> Runs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Lightweight per-run projection for the run-group list cards — just the fields the left-rail
/// ModelStack renders (endpoint + pass/fail counts). Omits the fat <see cref="TestRunDto"/>'s
/// per-case results, test cases, and evaluations.
/// </summary>
public record TestRunSummaryDto(
    Guid Id,
    Guid EndpointId,
    string EndpointName,
    int SampleIndex,
    TestRunStatus Status,
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double PassRate);

/// <summary>
/// Lightweight run-group projection for the runs list. Carries only what the left-rail card needs;
/// the full <see cref="TestRunGroupDto"/> (with nested per-case results) is fetched per-selection
/// via <c>GET /api/test-run-groups/{id}</c>.
/// </summary>
public record TestRunGroupListItemDto(
    Guid Id,
    Guid SuiteId,
    string SuiteName,
    Guid AgentId,
    string AgentName,
    TestRunStatus Status,
    bool IsSystemRun,
    int SampleCount,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TestRunSummaryDto> Runs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Request payload for create test run group operations.
/// </summary>
public record CreateTestRunGroupRequest(
    Guid TestSuiteId,
    IReadOnlyList<Guid> ModelEndpointIds,
    int SampleCount = 1);
