namespace Proxytrace.Storage.Internal.Entities.Statistics;

internal record TestRunStatsEntity : Entity
{
    /// <summary>
    /// Gets or sets the test run id.
    /// </summary>
    public required Guid TestRunId { get; init; }
    /// <summary>
    /// Gets or sets the agent id.
    /// </summary>
    public required Guid AgentId { get; init; }
    /// <summary>
    /// Gets or sets the endpoint id.
    /// </summary>
    public required Guid EndpointId { get; init; }
    /// <summary>
    /// Gets or sets the group id.
    /// </summary>
    public required Guid GroupId { get; init; }
    /// <summary>
    /// Gets or sets the suite id.
    /// </summary>
    public required Guid SuiteId { get; init; }
    /// <summary>
    /// Gets or sets the test cases.
    /// </summary>
    public required int TestCases { get; init; }
    /// <summary>
    /// Gets or sets the passed.
    /// </summary>
    public required int Passed { get; init; }
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
    /// Gets or sets the total duration microseconds.
    /// </summary>
    public long? TotalDurationMicroseconds { get; init; }
    /// <summary>
    /// Gets or sets the cost.
    /// </summary>
    public decimal? Cost { get; init; }
    /// <summary>
    /// Gets or sets the run completed at.
    /// </summary>
    public required DateTimeOffset RunCompletedAt { get; init; }
}
