using System.Text.Json;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.TestResult;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Application.Optimization.Internal.Evidence;

internal sealed record OptimizerEvidence(
    IAgent Agent,
    IReadOnlyList<ITestResult> Failing,
    IReadOnlyList<ITestResult> PassingSample)
{
    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// To json.
    /// </summary>
    public string ToJson()
    {
        var payload = new
        {
            agent = new
            {
                name = Agent.Name,
                system_prompt = Agent.SystemPrompt.Template,
                tools = Agent.Tools.Select(ToToolPayload).ToArray(),
            },
            failing = Failing.Select(ToResultPayload).ToArray(),
            passing = PassingSample.Select(ToResultPayload).ToArray(),
        };

        return JsonSerializer.Serialize(payload, serializerOptions);
    }

    private static object ToToolPayload(ToolSpecification tool)
    {
        using var doc = TryParseJson(tool.Arguments.JsonSchema);
        return new
        {
            name = tool.Name,
            description = tool.Description,
            parameters = doc is not null
                ? JsonSerializer.Deserialize<JsonElement>(tool.Arguments.JsonSchema)
                : (object?)null,
        };
    }

    private static object ToResultPayload(ITestResult result)
    {
        return new
        {
            input = result.TestCase.Input.Messages,
            expected = result.TestCase.ExpectedOutput,
            actual = result.ActualResponse,
            passed = result.Passed,
            overall_score = result.OverallScore?.ToString(),
            evaluations = result.Evaluations.Select(e => new
            {
                evaluator = e.Evaluator.Kind.ToString(),
                score = e.Score.ToString(),
                passed = e.Passed,
                reasoning = e.Reasoning,
            }).ToArray(),
        };
    }

    private static JsonDocument? TryParseJson(string raw)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
