using Proxytrace.Domain.TestRun;

namespace Proxytrace.Api.Dto.Proposals;

/// <summary>
/// Data transfer object representing a ab test run summary.
/// </summary>
public record AbTestRunSummaryDto(
    Guid Id,
    Guid GroupId,
    TestRunStatus Status,
    int TotalCases,
    int CompletedCases,
    int PassedCases,
    int FailedCases,
    double PassRate,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    // Average per-case model inference latency over the run's results (aggregated inference latency,
    // NOT a wall-clock CompletedAt - StartedAt timer). Null until the run has at least one result.
    long? DurationMs);
