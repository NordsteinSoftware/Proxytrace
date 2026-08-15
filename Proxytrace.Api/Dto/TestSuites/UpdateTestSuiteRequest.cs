using System.ComponentModel.DataAnnotations;

namespace Proxytrace.Api.Dto.TestSuites;

/// <summary>
/// Request payload for update test suite operations.
/// </summary>
public record UpdateTestSuiteRequest(
    Guid? AgentId,
    [MaxLength(RequestLimits.MaxEvaluators)] IReadOnlyList<Guid>? EvaluatorIds,
    [MaxLength(RequestLimits.MaxTestCases)] IReadOnlyList<Guid>? TestCaseIds);
