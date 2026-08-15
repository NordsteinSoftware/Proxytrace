namespace Proxytrace.Messaging;

/// <summary>
/// Specifies the messaging provider.
/// </summary>
public enum MessagingProvider
{
    /// <summary>In-memory channel. Single process only — used by the test suite and local runs.</summary>
    InProcess,

    /// <summary>Redis Streams. Used for the proxy/app split deployment.</summary>
    Redis,
}

/// <summary>
/// Configuration for messaging.
/// </summary>
public sealed record MessagingConfiguration
{
    /// <summary>
    /// Gets or sets the provider.
    /// </summary>
    public MessagingProvider Provider { get; init; } = MessagingProvider.InProcess;

    /// <summary>
    /// Gets or sets the redis connection string.
    /// </summary>
    public string RedisConnectionString { get; init; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the stream.
    /// </summary>
    public string Stream { get; init; } = "proxytrace:ingest";

    /// <summary>
    /// Gets or sets the consumer group.
    /// </summary>
    public string ConsumerGroup { get; init; } = "proxytrace-app";

    /// <summary>Per-instance consumer name within the group; defaults to the machine name.</summary>
    public string ConsumerName { get; init; } = Environment.MachineName;

    /// <summary>
    /// Idle time after which a pending entry is reclaimed via XAUTOCLAIM. This must stay far above the
    /// worst-case time to persist a single captured call (parse → agent/version reconcile → AgentCall
    /// write → SSE broadcast → outlier eval), which completes in well under a second normally and a
    /// few seconds even under contention. Sizing it an order of magnitude above that worst case means
    /// reclaim only ever fires for a genuinely dead/crashed consumer — never for a slow-but-live
    /// persist, which a shorter window would reclaim and double-process into a duplicate trace (#261).
    /// It is also the redelivery interval for a retryable failure left unacked, so it is not set
    /// arbitrarily high. The ingestion worker additionally dedups overlapping reclaims in-process.
    /// </summary>
    public int ReclaimIdleMs { get; init; } = 300_000;

    /// <summary>
    /// Approximate cap on the number of entries retained in the Redis stream (XADD MAXLEN ~). Bounds
    /// Redis memory if the consumer falls behind or the app is down; acknowledged entries are also
    /// trimmed. Generous so a healthy consumer never loses unprocessed entries.
    /// </summary>
    public int MaxStreamLength { get; init; } = 1_000_000;

    /// <summary>
    /// How many entries the consumer reads per <c>XREADGROUP</c>/<c>XAUTOCLAIM</c> round. Larger
    /// batches amortise the round-trip and feed the parallel processor; the worker still acks each
    /// entry individually.
    /// </summary>
    public int BatchSize { get; init; } = 64;

    /// <summary>
    /// Maximum number of captured calls the ingestion worker persists concurrently. Each unit of
    /// work runs on its own async flow (own DbContext via the AsyncLocal ambient context), so raising
    /// this lifts write throughput past the one-trace-at-a-time ceiling.
    /// <para>
    /// Defaults to 1 (serial). Concurrency &gt; 1 is only safe on a transport that redelivers
    /// unacknowledged entries (Redis Streams): a retryable race — e.g. two concurrent calls
    /// appending a version to the same agent — is requeued and reprocessed. The in-process channel
    /// does not redeliver, so it must stay serial; the Redis composition root raises this.
    /// </para>
    /// </summary>
    public int MaxConcurrency { get; init; } = 1;
}
