namespace Proxytrace.Application.Evaluator;

/// <summary>
/// Provides the built-in judge prompts that are auto-provisioned as agentic evaluators for every new project.
/// </summary>
public interface IAgenticEvaluatorPresets
{
    /// <summary>
    /// Returns all registered presets, each of which is seeded as a default evaluator on project creation.
    /// </summary>
    IReadOnlyList<AgenticEvaluatorPreset> GetAll();
}

/// <summary>
/// A single built-in evaluator template: its stable key, display name, and the judge-LLM system prompt.
/// </summary>
public sealed record AgenticEvaluatorPreset(string Key, string Name, string SystemPrompt);
