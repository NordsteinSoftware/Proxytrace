namespace Proxytrace.Application.Evaluator.Internal;

internal sealed class AgenticEvaluatorPresets : IAgenticEvaluatorPresets
{
    private static readonly IReadOnlyList<AgenticEvaluatorPreset> Presets =
    [
        new("helpfulness", "Helpfulness", Prompts.helpfulness_evaluator),
        new("politeness", "Politeness", Prompts.politeness_evaluator),
        new("safety", "Safety Classifier", Prompts.safety_classifier),
        new("tool_usage", "Tool Usage", Prompts.tool_usage_evaluator),
    ];

    /// <summary>
    /// Gets the all.
    /// </summary>
    public IReadOnlyList<AgenticEvaluatorPreset> GetAll() => Presets;
}
