using Proxytrace.Domain.Evaluation;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

[StoredDomainEntity(typeof(ITestResult))]
internal record TestResultEntity : Entity
{
    public required Guid TestCase { get; init; }
    public required AssistantMessage ActualResponse { get; init; }
    public required IReadOnlyCollection<StoredEvaluation> Evaluations { get; init; }
    public required long DurationMs { get; init; }
    public required long? InputTokens { get; init; }
    public required long? OutputTokens { get; init; }
    public required long? CachedInputTokens { get; init; }

    /// <summary>
    /// Queryable projection of <see cref="Evaluations"/>, one row per evaluation, populated at write
    /// time. Lets the evaluator-stats queries filter by evaluator and time in SQL instead of loading
    /// and deserializing every result in the window. Not loaded on the read path (the JSON
    /// <see cref="Evaluations"/> column remains the source of truth). See <see cref="EvaluationStatEntity"/>.
    /// </summary>
    public ICollection<EvaluationStatEntity> EvaluationStats { get; init; } = [];
}

/// <summary>
/// Storage-only value object for serializing an evaluation into the TestResult row.
/// </summary>
internal record StoredEvaluation
{
    public required Guid EvaluatorId { get; init; }
    public EvaluationScore? Score { get; init; }
    public string? Reasoning { get; init; }
    public string? ErrorMessage { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? CachedInputTokens { get; init; }
    public long LatencyMicroseconds { get; init; }
    public decimal? Cost { get; init; }
}
