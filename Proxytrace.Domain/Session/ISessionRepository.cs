namespace Proxytrace.Domain.Session;

/// <summary>
/// The counters one session lost when some of its traces were deleted — the exact reversal of the
/// bumps <see cref="ISessionRepository.RecordActivityAsync"/> applied when they arrived.
/// </summary>
public readonly record struct SessionTraceRemoval(Guid SessionId, int TraceCount, long TotalTokens);

public interface ISessionRepository : IRepository<ISession>
{
    /// <summary>
    /// Ingestion-hot-path upsert: creates the session on first sight, otherwise bumps
    /// LastActivityAt / TraceCount / TotalTokens. Safe under concurrent ingestion.
    /// Must NOT be called inside an ambient transaction (ITransaction.InvokeAsync): its
    /// lost-first-insert recovery relies on a fresh context per attempt, and inside an aborted
    /// Postgres transaction the recovery bump can never succeed.
    /// </summary>
    Task RecordActivityAsync(
        Guid sessionId,
        string externalKey,
        Guid projectId,
        long totalTokens,
        DateTimeOffset lastActivityAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverses the counter bumps of <see cref="RecordActivityAsync"/> for traces that have since
    /// been deleted (retention, or a single trace removed by hand). Without it the denormalized
    /// counters only ever grow, so a session header claims more traces than its timeline can show
    /// and the drift is permanent.
    ///
    /// Best-effort and idempotent-ish in the same sense as the bump: both counters are clamped at
    /// zero, so a delta that overshoots (a redelivered ingest, a pre-existing drift) settles the row
    /// at "none left" rather than going negative. Deletes nothing — a session whose traces have all
    /// aged out is removed by <see cref="RemoveOlderThanAsync"/>, which cannot strand a session that
    /// still has recent traces.
    /// </summary>
    Task RecordTraceRemovalsAsync(
        IReadOnlyCollection<SessionTraceRemoval> removals,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes sessions whose <see cref="ISession.LastActivityAt"/> is at or before
    /// <paramref name="cutoff"/>, and returns how many were removed. Called by trace retention with
    /// the same cutoff: a session's last activity *is* its newest trace, so this deletes exactly the
    /// sessions whose every trace has just been (or already was) retained away — otherwise session
    /// rows accumulate forever for clients that mint a fresh key per run.
    /// </summary>
    Task<int> RemoveOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Recent sessions of a project, most recently active first.</summary>
    Task<(IReadOnlyList<ISession> Items, int Total)> GetRecentAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
