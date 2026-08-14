using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Evaluation;

public interface IEvaluation : IDomainObject
{
    public delegate IEvaluation Create(
        IEvaluator evaluator,
        EvaluationScore score,
        TimeSpan latency,
        TokenUsage? tokenUsage = null,
        decimal? cost = null,
        string? reasoning = null);

    public delegate IEvaluation CreateErrored(
        IEvaluator evaluator,
        TimeSpan latency,
        Exception exception);

    /// <summary>
    /// The <see cref="IEvaluator"/>
    /// </summary>
    IEvaluator Evaluator { get; }

    /// <summary>
    /// The score assigned by the evaluator to the test result, based on the evaluation strategy.
    /// Higher is better.
    /// </summary>
    EvaluationScore? Score { get; }

    /// <summary>
    /// Whether the evaluation has passed
    /// </summary>
    bool Passed { get; }

    /// <summary>
    /// A short explanation of the evaluation
    /// </summary>
    string? Reasoning { get; }

    /// <summary>
    /// Error description
    /// </summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Total wall-clock time spent producing this evaluation.
    /// </summary>
    TimeSpan Latency { get; }

    /// <summary>
    /// Token usage attributable to this evaluation. Null for evaluators that do not invoke an LLM
    /// (e.g. exact match, JSON schema match, numeric match).
    /// </summary>
    TokenUsage? TokenUsage { get; }

    /// <summary>
    /// Cost of the LLM call backing this evaluation, snapshot at the time of evaluation using the
    /// judge endpoint's per-token cost. Null for non-LLM evaluators.
    /// </summary>
    decimal? Cost { get; }
}
