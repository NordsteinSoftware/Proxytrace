namespace Proxytrace.Application.Evaluator;

/// <summary>
/// Represents a agentic evaluator presets.
/// </summary>
public interface IAgenticEvaluatorPresets
{
    IReadOnlyList<AgenticEvaluatorPreset> GetAll();
}

/// <summary>
/// Represents a agentic evaluator preset.
/// </summary>
public sealed record AgenticEvaluatorPreset(string Key, string Name, string SystemPrompt);
