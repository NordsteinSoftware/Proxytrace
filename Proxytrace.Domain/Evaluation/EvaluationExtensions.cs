namespace Proxytrace.Domain.Evaluation;

/// <summary>
/// Extensions for <see cref="IEvaluation"/>
/// </summary>
internal static class EvaluationExtensions
{
    /// <summary>
    /// Whether the evaluation never produced a verdict — the evaluator threw, or its judge answered
    /// something that could not be read. Distinct from a verdict of "did not pass": an errored
    /// evaluation is evidence about the evaluator, not about the agent under test.
    /// </summary>
    public static bool IsErrored(this IEvaluation evaluation)
        => !string.IsNullOrWhiteSpace(evaluation.ErrorMessage);

    /// <summary>
    /// Combines the scores.
    /// </summary>
    public static EvaluationScore? CombineScores(this IReadOnlyCollection<IEvaluation> evaluations)
    {
        var scored = evaluations
            .Where(x => x is { ErrorMessage: null, Score: not null })
            .Select(x => (byte)(x.Score ?? 0))
            .ToArray();

        if (scored.Length == 0)
        {
            return null;
        }
        return (EvaluationScore)Math.Round(scored.Average(b => (double)b));
    }
}
