namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Specifies the evaluator kind.
/// </summary>
public enum EvaluatorKind
{
    Agentic = 0,
    ExactMatch = 1,
    NumericMatch = 2,
    JsonSchemaMatch = 3,
}
