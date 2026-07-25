namespace Proxytrace.Domain.Session;

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

    /// <summary>Recent sessions of a project, most recently active first.</summary>
    Task<(IReadOnlyList<ISession> Items, int Total)> GetRecentAsync(
        Guid projectId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
