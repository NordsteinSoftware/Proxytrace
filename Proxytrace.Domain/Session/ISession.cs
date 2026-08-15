namespace Proxytrace.Domain.Session;

/// <summary>
/// A debugging session: a client-chosen grouping of traces (one app run / user session) that can
/// span multiple agents and conversations. Auto-created at ingestion from the
/// x-proxytrace-session-id header; <see cref="LastActivityAt"/> and the counters are denormalized
/// there so session lists never aggregate the high-volume traces table.
/// </summary>
public interface ISession : IDomainEntity<ISession>
{
    const int MaxExternalKeyLength = 200;

    /// <summary>The raw client-supplied session key (truncated to <see cref="MaxExternalKeyLength"/>).</summary>
    string ExternalKey { get; }

    Guid ProjectId { get; }

    DateTimeOffset LastActivityAt { get; }

    int TraceCount { get; }

    long TotalTokens { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ISession CreateNew(
        string externalKey,
        Guid projectId,
        DateTimeOffset lastActivityAt,
        int traceCount,
        long totalTokens);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ISession CreateExisting(
        string externalKey,
        Guid projectId,
        DateTimeOffset lastActivityAt,
        int traceCount,
        long totalTokens,
        IDomainEntityData existing);
}
