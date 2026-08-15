using Proxytrace.Domain.Evaluation;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

[StoredDomainEntity(typeof(ITestResult))]
internal record TestResultEntity : Entity
{
    /// <summary>
    /// Gets or sets the test case.
    /// </summary>
    public required Guid TestCase { get; init; }
    /// <summary>
    /// Gets or sets the actual response.
    /// </summary>
    public required AssistantMessage ActualResponse { get; init; }
    /// <summary>
    /// Gets or sets the evaluations.
    /// </summary>
    public required IReadOnlyCollection<StoredEvaluation> Evaluations { get; init; }
    /// <summary>
    /// Gets or sets the duration ms.
    /// </summary>
    public required long DurationMs { get; init; }
    /// <summary>
    /// Gets or sets the input tokens.
    /// </summary>
    public required long? InputTokens { get; init; }
    /// <summary>
    /// Gets or sets the output tokens.
    /// </summary>
    public required long? OutputTokens { get; init; }
    /// <summary>
    /// Gets or sets the cached input tokens.
    /// </summary>
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
    /// <summary>
    /// Gets or sets the evaluator id.
    /// </summary>
    public required Guid EvaluatorId { get; init; }
    /// <summary>
    /// Gets or sets the score.
    /// </summary>
    public EvaluationScore? Score { get; init; }
    /// <summary>
    /// Gets or sets the reasoning.
    /// </summary>
    public string? Reasoning { get; init; }
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string? ErrorMessage { get; init; }
    /// <summary>
    /// Gets or sets the input tokens.
    /// </summary>
    public long? InputTokens { get; init; }
    /// <summary>
    /// Gets or sets the output tokens.
    /// </summary>
    public long? OutputTokens { get; init; }
    /// <summary>
    /// Gets or sets the cached input tokens.
    /// </summary>
    public long? CachedInputTokens { get; init; }
    /// <summary>
    /// Gets or sets the latency microseconds.
    /// </summary>
    public long LatencyMicroseconds { get; init; }
    /// <summary>
    /// Gets or sets the cost.
    /// </summary>
    public decimal? Cost { get; init; }
}
