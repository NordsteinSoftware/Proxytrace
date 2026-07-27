using System.ComponentModel.DataAnnotations;

namespace Proxytrace.Api.Dto.TestSuites;

public record UpdateTestSuiteRequest(
    Guid? AgentId,
    [MaxLength(RequestLimits.MaxEvaluators)] IReadOnlyList<Guid>? EvaluatorIds,
    [MaxLength(RequestLimits.MaxTestCases)] IReadOnlyList<Guid>? TestCaseIds);
