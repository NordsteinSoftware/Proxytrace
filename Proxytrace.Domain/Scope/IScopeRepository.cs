namespace Proxytrace.Domain.Scope;

/// <summary>
/// The counters one scope member (an agent version) lost when some of its scoped traces were
/// deleted — the exact reversal of the bumps <see cref="IScopeRepository.RecordActivityAsync"/>
/// applied when they arrived.
/// </summary>
public readonly record struct ScopeTraceRemoval(Guid ScopeId, Guid AgentVersionId, int TraceCount, long TotalTokens);

/// <summary>Activity of one agent inside a scope, summed over the agent's versions.</summary>
public sealed record ScopeAgentStat(
    Guid AgentId,
    int TraceCount,
    long TotalTokens,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

/// <summary>
/// A scope with its within-retention activity. Counters are maintained best-effort at ingestion
/// (like a session's), so they are approximate; exact time-ranged numbers come from the trace and
/// statistics queries filtered by <c>ScopeId</c>.
/// </summary>
public sealed record ScopeOverview(
    IScope Scope,
    int TraceCount,
    long TotalTokens,
    DateTimeOffset? LastActivityAt,
    IReadOnlyList<ScopeAgentStat> Agents);

/// <summary>
/// Repository for persisting and querying scopes and their derived agent membership.
/// </summary>
public interface IScopeRepository : IRepository<IScope>
{
    /// <summary>
    /// Ingestion-hot-path admission: returns the id of the project's scope with the canonical
    /// <paramref name="externalKey"/>, creating it on first sight. Returns <see langword="null"/>
    /// when the scope does not exist yet and the project already holds
    /// <paramref name="maxScopesPerProject"/> scopes — the call is then ingested unscoped rather than
    /// letting a client that mints a key per user grow the table without bound. Safe under
    /// concurrent ingestion; must NOT run inside an ambient transaction (same reason as
    /// <c>ISessionRepository.RecordActivityAsync</c>).
    /// </summary>
    Task<Guid?> AdmitAsync(
        Guid projectId,
        string externalKey,
        int maxScopesPerProject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one scoped trace of <paramref name="agentVersionId"/>: creates the membership on first
    /// sight, otherwise bumps its counters and moves its last-seen time forward. Best-effort, safe
    /// under concurrent ingestion, and never inside an ambient transaction.
    /// </summary>
    Task RecordActivityAsync(
        Guid scopeId,
        Guid agentVersionId,
        long totalTokens,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The batch form of <see cref="RecordActivityAsync"/> for a producer that pre-aggregated its
    /// calls (demo seeding): records <paramref name="traceCount"/> traces seen between
    /// <paramref name="firstSeenAt"/> and <paramref name="lastSeenAt"/> in one write.
    /// </summary>
    Task RecordActivitiesAsync(
        Guid scopeId,
        Guid agentVersionId,
        int traceCount,
        long totalTokens,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the counter bumps of <see cref="RecordActivityAsync"/> for deleted traces. Both
    /// counters are clamped at zero, mirroring <c>ISessionRepository.RecordTraceRemovalsAsync</c>.
    /// </summary>
    Task RecordTraceRemovalsAsync(
        IReadOnlyCollection<ScopeTraceRemoval> removals,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes memberships last seen at or before <paramref name="cutoff"/> — run by trace retention
    /// with the same cutoff, so membership never outlives the traces it was derived from. The scope
    /// rows themselves are kept: their display name and description are user-curated.
    /// </summary>
    Task<int> RemoveMembershipsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>All scopes of a project with their activity, most recently active first.</summary>
    Task<IReadOnlyList<ScopeOverview>> GetOverviewsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>One scope with its activity, or <see langword="null"/> when it does not exist.</summary>
    Task<ScopeOverview?> GetOverviewAsync(Guid scopeId, CancellationToken cancellationToken = default);

    /// <summary>The agents that have served <paramref name="scopeId"/> within retention.</summary>
    Task<IReadOnlySet<Guid>> GetAgentIdsAsync(Guid scopeId, CancellationToken cancellationToken = default);
}
