namespace Proxytrace.Storage.Internal.Entities.Scope;

/// <summary>
/// Derived membership of an agent version in a scope, with its within-retention activity counters —
/// upserted at ingestion so the scopes page never aggregates the high-volume traces table.
/// Storage-only, no domain counterpart. Keyed by the agent *version* rather than the agent because
/// a version can be moved to another agent: the version's traces follow it, and so does its
/// membership, since agent membership is resolved through <c>AgentVersionEntity.AgentId</c> at read
/// time.
/// </summary>
internal record ScopeAgentVersionEntity
{
    /// <summary>
    /// Gets or sets the scope id.
    /// </summary>
    public required Guid ScopeId { get; init; }
    /// <summary>
    /// Gets or sets the agent version id.
    /// </summary>
    public required Guid AgentVersionId { get; init; }
    /// <summary>
    /// Gets or sets when the first scoped trace of this version arrived.
    /// </summary>
    public required DateTimeOffset FirstSeenAt { get; init; }
    /// <summary>
    /// Gets or sets when the latest scoped trace of this version arrived.
    /// </summary>
    public required DateTimeOffset LastSeenAt { get; init; }
    /// <summary>
    /// Gets or sets the trace count.
    /// </summary>
    public required int TraceCount { get; init; }
    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public required long TotalTokens { get; init; }
}
