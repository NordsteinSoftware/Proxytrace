using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Api.Dto.Evaluators;

/// <summary>
/// Maps <see cref="IEvaluator"/> and recent <see cref="ITestResult"/> records to evaluator DTOs.
/// </summary>
public sealed class EvaluatorDtoMapper
{
    /// <summary>
    /// To dto.
    /// </summary>
    public EvaluatorDetailDto ToDto(IEvaluator evaluator)
    {
        string? systemMessage = null;
        string? jsonSchema = null;
        string? extractionPattern = null;
        decimal? tolerance = null;
        Guid? agentId = null;

        switch (evaluator)
        {
            case IAgenticEvaluator agentic:
                systemMessage = agentic.Agent.SystemPrompt.Template;
                agentId = agentic.Agent.Id;
                break;
            case IJsonSchemaMatchEvaluator jsonSchemaEval:
                jsonSchema = jsonSchemaEval.JsonSchema;
                break;
            case INumericMatchEvaluator numericEval:
                extractionPattern = numericEval.ExtractionPattern.ToString();
                tolerance = numericEval.Tolerance;
                break;
        }

        var endpoint = evaluator.Project.SystemEndpoint;

        return new EvaluatorDetailDto(
            evaluator.Id,
            evaluator.Kind,
            evaluator.Name,
            systemMessage,
            evaluator.Project.Id,
            evaluator.Project.Name,
            endpoint.Id,
            endpoint.Model.Name,
            agentId,
            jsonSchema,
            extractionPattern,
            tolerance,
            evaluator.CreatedAt,
            evaluator.UpdatedAt);
    }

    /// <summary>
    /// To recent dto.
    /// </summary>
    public RecentEvaluationItemDto ToRecentDto(ITestResult r, Guid evaluatorId, Guid? runId)
    {
        var evaluation = r.Evaluations.FirstOrDefault(e => e.Evaluator.Id == evaluatorId);
        return new RecentEvaluationItemDto(
            TestResultId: r.Id,
            TestCaseId: r.TestCase.Id,
            CaseSummary: r.TestCase.GetSummary(),
            Score: evaluation?.Score?.ToString(),
            Passed: evaluation?.Passed ?? r.Passed,
            Reasoning: evaluation?.Reasoning,
            LatencyMs: (int)r.Latency.TotalMilliseconds,
            EvaluatedAt: r.UpdatedAt,
            RunId: runId);
    }
}
