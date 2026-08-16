using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Evaluation.Internal;

internal sealed record Evaluation : IEvaluation
{
    /// <summary>
    /// Gets the evaluator.
    /// </summary>
    public IEvaluator Evaluator { get; }
    /// <summary>
    /// Gets the score.
    /// </summary>
    public EvaluationScore? Score { get; }

    /// <summary>
    /// The passed.
    /// </summary>
    public bool Passed =>
        string.IsNullOrWhiteSpace(ErrorMessage)
        && Score is >= EvaluationScore.Acceptable;

    /// <summary>
    /// Gets the reasoning.
    /// </summary>
    public string? Reasoning { get; }
    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string? ErrorMessage { get; }
    /// <summary>
    /// Gets the latency.
    /// </summary>
    public TimeSpan Latency { get; }
    /// <summary>
    /// Gets the token usage.
    /// </summary>
    public TokenUsage? TokenUsage { get; }
    /// <summary>
    /// Gets the cost.
    /// </summary>
    public decimal? Cost { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Evaluation"/> class.
    /// </summary>
    public Evaluation(
        IEvaluator evaluator,
        EvaluationScore score,
        TimeSpan latency,
        TokenUsage? tokenUsage = null,
        decimal? cost = null,
        string? reasoning = null)
    {
        Evaluator = evaluator;
        Score = score;
        Latency = latency;
        TokenUsage = tokenUsage;
        Cost = cost;
        Reasoning = reasoning;
        ErrorMessage = null;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Evaluation"/> class.
    /// </summary>
    public Evaluation(
        IEvaluator evaluator,
        TimeSpan latency,
        Exception exception)
    {
        Evaluator = evaluator;
        Score = null;
        Latency = latency;
        TokenUsage = null;
        Cost = null;
        Reasoning = null;
        ErrorMessage = exception is StoredEvaluationException
            ? exception.Message
            : $"{exception.GetType().Name}: {exception.Message}";
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in Evaluator.Validate(validationContext))
        {
            yield return validationResult;
        }

        if (Score.HasValue)
        {
            yield return Validation.Defined(Score.Value);
        }
    }
}
