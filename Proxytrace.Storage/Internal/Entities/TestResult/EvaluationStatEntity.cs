using Proxytrace.Domain.Evaluation;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

/// <summary>
/// Query-optimized projection of a single evaluation into a queryable child row of
/// <see cref="TestResultEntity"/>. The authoritative copy of an evaluation stays in the
/// JSON-serialized <see cref="TestResultEntity.Evaluations"/> column; this row exists only so the
/// evaluator-scoped statistics queries (<c>EvaluatorStatsQueries</c>) can filter by
/// <see cref="EvaluatorId"/> and time <em>in SQL</em> instead of loading and deserializing every
/// test result in the window. Storage-only, no domain counterpart.
/// <para>
/// Populated at write time from <c>TestResultConfig.Map</c>. Like the AgentCall
/// <c>RequestPreview</c> denormalization, test results written before this table existed carry no
/// projection row; <c>EvaluationStatBackfillService</c> rebuilds those rows at startup (from the
/// authoritative JSON evaluations) so historical results appear in evaluator statistics too.
/// </para>
/// </summary>
internal record EvaluationStatEntity
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>The owning <see cref="TestResultEntity"/>.</summary>
    public required Guid TestResultId { get; init; }

    /// <summary>
    /// Gets or sets the evaluator id.
    /// </summary>
    public required Guid EvaluatorId { get; init; }

    /// <summary>
    /// Copied from the parent <see cref="TestResultEntity.CreatedAt"/> so the time-bucketed stats
    /// queries range and group on this row directly, with no join back to the result.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the score.
    /// </summary>
    public EvaluationScore? Score { get; init; }

    /// <summary>True for an errored evaluation (no score); mirrors a non-null
    /// <c>StoredEvaluation.ErrorMessage</c>. The error text itself is not projected.</summary>
    public bool HasError { get; init; }

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
