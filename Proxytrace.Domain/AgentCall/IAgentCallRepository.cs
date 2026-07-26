using Proxytrace.Domain.Project;
using Proxytrace.Domain.Session;

namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Repository for <see cref="IAgentCall"/> entities with paginated filtering support.
/// </summary>
public interface IAgentCallRepository : IRepository<IAgentCall>
{
    /// <summary>
    /// Returns a paginated, filtered list of agent calls together with the total count of matching records.
    /// </summary>
    Task<(IReadOnlyList<IAgentCall> Items, int Total)> GetFilteredAsync(
        AgentCallFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Same filter/paging as <see cref="GetFilteredAsync"/> but returns the lightweight
    /// <see cref="AgentCallListItem"/> projection for the traces table: the query reads only scalar
    /// row columns (never the request/response/model-parameter payloads) so a page does not
    /// deserialise — nor ship over the wire — potentially huge conversation JSON for every row.
    /// </summary>
    Task<(IReadOnlyList<AgentCallListItem> Items, int Total)> GetFilteredListAsync(
        AgentCallFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Buckets matching calls into <paramref name="buckets"/> equal-width time slots spanning the
    /// filter window. When <see cref="AgentCallFilter.From"/> is null the window starts at the
    /// earliest matching call; when <see cref="AgentCallFilter.To"/> is null it ends at "now".
    /// Returns an empty list when nothing matches.
    /// </summary>
    Task<IReadOnlyList<AgentCallHistogramBucket>> GetHistogramAsync(
        AgentCallFilter filter,
        int buckets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates every call matching <paramref name="filter"/> into the traces KPI summary — counts,
    /// token sums, cost, latency mean/standard deviation, and error count. Unpaged by design: the
    /// traces table scrolls rather than pages, so its KPI band describes the whole filtered set.
    /// Runs as a single grouped aggregate (one row per endpoint, because cost is priced per
    /// endpoint), so it stays O(endpoints) over the wire no matter how many calls match.
    /// </summary>
    Task<AgentCallSummary> GetSummaryAsync(
        AgentCallFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the timestamp of the most recent call for each agent, keyed by agent ID.
    /// Agents with no calls are omitted.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetLastCallTimesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recently created call for the given conversation, or <see langword="null"/> if none exists.
    /// </summary>
    Task<IAgentCall?> FindLatestByConversationIdAsync(
        Guid conversationId,
        IProject project,
        CancellationToken cancellationToken = default);

    Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken);

    /// <summary>
    /// Per-session totals of the calls <see cref="RemoveOlderThanAsync"/> would delete at the same
    /// cutoff — the deltas retention hands to <c>ISessionRepository.RecordTraceRemovalsAsync</c> so
    /// the denormalized session counters stay honest. Must be read *before* the delete; afterwards
    /// the rows are gone.
    ///
    /// Aggregated in the database (a <c>GROUP BY SessionId</c> over the same indexed
    /// <c>CreatedAt</c> range the delete uses), so what crosses the wire is O(sessions in the
    /// window), never O(rows). Calls with no session are excluded.
    /// </summary>
    Task<IReadOnlyList<SessionTraceRemoval>> GetSessionRemovalsOlderThanAsync(
        DateTimeOffset cutoffDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ORs <paramref name="flag"/> into the call's <see cref="IAgentCall.OutlierFlags"/> bitmask,
    /// preserving any bits already set. Used by the asynchronous custom-anomaly review to flag a
    /// call after ingestion. A no-op when the call no longer exists.
    /// </summary>
    Task SetOutlierFlagAsync(Guid id, OutlierFlags flag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct tool names requested by any call in the given project, sorted
    /// alphabetically. Backs the traces filter's tool-name picker. When <paramref name="agentId"/>
    /// is supplied, the result is scoped to that agent's calls — so an active agent filter only
    /// offers tools that agent actually used.
    /// </summary>
    Task<IReadOnlyList<string>> GetToolNamesAsync(
        Guid projectId, Guid? agentId = null, CancellationToken cancellationToken = default);
}
