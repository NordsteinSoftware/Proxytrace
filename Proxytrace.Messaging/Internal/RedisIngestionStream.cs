using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Proxytrace.Messaging.Internal;

/// <summary>
/// Redis Streams transport. The proxy <c>XADD</c>s captured calls; the app reads them through a
/// consumer group (<c>XREADGROUP</c>) and acknowledges (<c>XACK</c>) only after successful
/// processing. Entries left pending by a crashed consumer are reclaimed via <c>XAUTOCLAIM</c>.
/// </summary>
/// <remarks>
/// Every optional argument on the StackExchange.Redis calls below is passed explicitly, including
/// the trailing <see cref="CommandFlags"/>. A release that appends a new optional parameter adds an
/// overload rather than extending the existing one (2.12 added a blocking-read <c>TimeSpan?</c> to
/// <c>StreamReadGroupAsync</c>, and a <c>StreamTrimMode</c> to <c>StreamAddAsync</c>), and an
/// under-specified call silently re-binds to it on upgrade. Supplying every parameter keeps overload
/// resolution pinned to the signature these calls were written against.
/// </remarks>
internal sealed class RedisIngestionStream : IIngestionStream
{
    private const string PayloadField = "payload";

    private readonly IConnectionMultiplexer connection;
    private readonly MessagingConfiguration configuration;
    private readonly ILogger<RedisIngestionStream> logger;

    public RedisIngestionStream(
        IConnectionMultiplexer connection,
        MessagingConfiguration configuration,
        ILogger<RedisIngestionStream> logger)
    {
        this.connection = connection;
        this.configuration = configuration;
        this.logger = logger;
    }

    private IDatabase Database => connection.GetDatabase();

    public async Task PublishAsync(IngestMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Publishing is fire-and-forget on the proxy hot path, and the producer awaits it inside the
        // `finally` of both response paths with CancellationToken.None — an uncancellable wait that a
        // client abort cannot release. The multiplexer is configured with AbortOnConnectFail=false, so
        // while Redis is down StackExchange.Redis backlogs the command until its async timeout (~5s)
        // rather than failing fast, adding that delay to *every* proxied response. Short-circuit on the
        // same IsConnected check GetQueueDepthAsync uses: drop the capture instead of stalling the
        // response the proxy exists to forward. Logged (never thrown) so an outage is not a silent loss.
        if (!connection.IsConnected)
        {
            logger.LogWarning(
                "Redis is not connected — dropping the captured call for project {ProjectId} rather than "
                + "stalling the proxy response until the command times out",
                message.ProjectId);
            return;
        }

        var payload = JsonSerializer.Serialize(message);
        // Approximate MAXLEN trim bounds Redis memory if the consumer lags or the app is down.
        await Database.StreamAddAsync(
            configuration.Stream,
            PayloadField,
            payload,
            messageId: null,
            maxLength: configuration.MaxStreamLength,
            useApproximateMaxLength: true,
            flags: CommandFlags.None);
    }

    public async IAsyncEnumerable<IngestEnvelope> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await EnsureGroupAsync();

        RedisValue reclaimCursor = "0-0";

        while (!cancellationToken.IsCancellationRequested)
        {
            // Reclaim entries stuck pending on a dead consumer before reading new ones.
            StreamAutoClaimResult claimed = await Database.StreamAutoClaimAsync(
                configuration.Stream,
                configuration.ConsumerGroup,
                configuration.ConsumerName,
                configuration.ReclaimIdleMs,
                reclaimCursor,
                count: configuration.BatchSize,
                flags: CommandFlags.None);
            reclaimCursor = claimed.NextStartId;

            var produced = false;
            await foreach (IngestEnvelope envelope in ToEnvelopesAsync(claimed.ClaimedEntries))
            {
                produced = true;
                yield return envelope;
            }

            StreamEntry[] entries = await Database.StreamReadGroupAsync(
                configuration.Stream,
                configuration.ConsumerGroup,
                configuration.ConsumerName,
                StreamPosition.NewMessages,
                count: configuration.BatchSize,
                noAck: false,
                flags: CommandFlags.None);

            await foreach (IngestEnvelope envelope in ToEnvelopesAsync(entries))
            {
                produced = true;
                yield return envelope;
            }

            if (!produced)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }

    public async Task AckAsync(string messageId, CancellationToken cancellationToken = default)
        => await Database.StreamAcknowledgeAsync(
            configuration.Stream,
            configuration.ConsumerGroup,
            messageId,
            flags: CommandFlags.None);

    // Pending entries are reclaimed via XAUTOCLAIM and redelivered, so an unacked envelope reappears.
    public bool RedeliversUnacknowledged => true;

    public async Task<long> GetQueueDepthAsync(CancellationToken cancellationToken = default)
    {
        // Depth is observability-only and runs on the hot dashboard path. The multiplexer is
        // configured with AbortOnConnectFail=false, so when Redis is down a command does not fail
        // fast — it blocks until ConnectTimeout (~5s) before throwing. Bail out the moment the
        // multiplexer reports no connection so a Redis outage never stalls every dashboard load.
        if (!connection.IsConnected)
        {
            return 0L;
        }

        try
        {
            StreamGroupInfo[] groups = await Database.StreamGroupInfoAsync(
                configuration.Stream,
                flags: CommandFlags.None);
            foreach (StreamGroupInfo group in groups)
            {
                if (group.Name != configuration.ConsumerGroup)
                {
                    continue;
                }

                // Lag = entries added but not yet delivered to the group (the true backlog). It can
                // be null when Redis cannot compute it (e.g. after the stream was trimmed past the
                // group's position); fall back to the in-flight pending count.
                return group.Lag ?? group.PendingMessageCount;
            }
        }
        catch (RedisException ex)
        {
            // Depth is observability-only — never let a transient Redis error fail the caller.
            logger.LogDebug(ex, "Unable to read ingestion queue depth");
        }
        return 0L;
    }

    private async IAsyncEnumerable<IngestEnvelope> ToEnvelopesAsync(StreamEntry[] entries)
    {
        List<RedisValue>? poisonIds = null;
        foreach (StreamEntry entry in entries)
        {
            IngestMessage? message = Deserialize(entry);
            if (message is null)
            {
                // Poison entry — collect its id and ack the whole batch once below, off the hot
                // yield path. Acking each entry inline with the blocking sync XACK let a burst of
                // poison entries during a Redis blip stall this single producer one ~ConnectTimeout
                // wait at a time, and a throw tore down the whole Parallel.ForEachAsync round.
                (poisonIds ??= new List<RedisValue>()).Add(entry.Id);
                continue;
            }

            yield return new IngestEnvelope(entry.Id.ToString(), message);
        }

        if (poisonIds is not null)
        {
            await AcknowledgePoisonAsync(poisonIds);
        }
    }

    private async Task AcknowledgePoisonAsync(List<RedisValue> poisonIds)
    {
        try
        {
            // One batched async XACK rather than a blocking call per entry: it never ties up a
            // thread and a single round-trip cannot serialise multiple ConnectTimeout waits.
            await Database.StreamAcknowledgeAsync(
                configuration.Stream,
                configuration.ConsumerGroup,
                poisonIds.ToArray(),
                flags: CommandFlags.None);
        }
        catch (RedisException ex)
        {
            // Best-effort cleanup: an unacked poison entry stays pending and is reclaimed via
            // XAUTOCLAIM, so we just retry the ack next round. Never let it wedge the consumer.
            logger.LogDebug(ex, "Unable to acknowledge {Count} poison ingestion entries", poisonIds.Count);
        }
    }

    private IngestMessage? Deserialize(StreamEntry entry)
    {
        try
        {
            RedisValue payload = entry[PayloadField];
            var payloadString = payload.ToString();
            return string.IsNullOrWhiteSpace(payloadString)
                ? null 
                : JsonSerializer.Deserialize<IngestMessage>(payloadString);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Discarding unparseable ingestion entry {MessageId}", entry.Id);
            return null;
        }
    }

    // Called at the start of every consume loop (including retries after a Redis blip) so the
    // group is recreated if a restarted Redis lost it.
    private async Task EnsureGroupAsync()
    {
        try
        {
            await Database.StreamCreateConsumerGroupAsync(
                configuration.Stream,
                configuration.ConsumerGroup,
                StreamPosition.Beginning,
                createStream: true,
                flags: CommandFlags.None);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer group already exists — fine.
        }
    }
}
