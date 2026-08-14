using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Evaluation.Internal;

internal sealed record Evaluation : IEvaluation
{
    public IEvaluator Evaluator { get; }
    public EvaluationScore? Score { get; }

    public bool Passed =>
        string.IsNullOrWhiteSpace(ErrorMessage)
        && Score is >= EvaluationScore.Acceptable;

    public string? Reasoning { get; }
    public string? ErrorMessage { get; }
    public TimeSpan Latency { get; }
    public TokenUsage? TokenUsage { get; }
    public decimal? Cost { get; }

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
