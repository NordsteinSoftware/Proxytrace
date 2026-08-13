using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Messaging;

namespace Proxytrace.Application.Ingestion.Internal;

/// <summary>
/// Consumer side of ingestion. Reads captured calls off the <see cref="IIngestionStream"/>
/// (Redis Streams in the split deployment, in-memory otherwise) and hands each one to the shared
/// <see cref="IIngestionExecutor"/>, which enforces the quota, re-hydrates the referenced
/// provider/project, and persists the call. Replaces the producer half of the old in-process
/// ingestor, which now lives in the proxy service. In-process producers (e.g. Tracey) call the
/// executor directly instead of round-tripping through the transport.
/// </summary>
internal sealed class AgentCallIngestionWorker : BackgroundService
{
    private readonly IIngestionStream stream;
    private readonly IIngestionExecutor executor;
    private readonly MessagingConfiguration messaging;
    private readonly ILogger<AgentCallIngestionWorker> logger;

    // Cap redelivery of a retryable-but-deterministically-failing message so it can't loop forever
    // and block the pending list. Keyed by transport message id; concurrent because envelopes are
    // now processed in parallel. Only retains currently-failing ids.
    private const int MaxRetryableAttempts = 5;
    private readonly ConcurrentDictionary<string, int> failedAttempts = new();

    // Single-instance, in-process dedup guard (issue #261). On the Redis transport the consumer runs
    // XAUTOCLAIM at the top of every round to reclaim entries stuck pending on a dead consumer. If a
    // persist ever ran longer than the reclaim idle window, the next round would reclaim and hand
    // this same worker the *still-in-flight* entry a SECOND time — producing a duplicate trace row, a
    // duplicate `trace-created` SSE event, and a duplicate outlier evaluation. There is no idempotency
    // key on the AgentCall row, and a content-unique index is unsafe (two legitimately identical calls
    // must both persist), so we dedup on the transport entry id instead.
    //
    // Dedup guarantee (single ingestion-worker instance):
    //   1. MessagingConfiguration.ReclaimIdleMs is sized far above the worst-case single-envelope
    //      persist time, so XAUTOCLAIM only ever targets a genuinely dead consumer — never a
    //      slow-but-live persist. This is the primary guard.
    //   2. This set records each entry id BEFORE processing and removes it only after the entry has
    //      left processing (acked, or deliberately left pending for a genuine redelivery). Any
    //      reclaimed duplicate whose id is still in flight is skipped, leaving the original in-flight
    //      unit to ack it exactly once. This closes the narrow window where a reclaim overlaps a
    //      persist that is just finishing.
    // A genuine redelivery (dead-consumer recovery, or a retryable failure left unacked) re-enters
    // with the id already cleared, so it is reprocessed as intended — only overlapping duplicates are
    // dropped. ConcurrentDictionary keeps step 2 correct under Parallel.ForEachAsync.
    private readonly ConcurrentDictionary<string, byte> inFlightEntries = new();

    // Backoff between inline retries on a non-redelivering transport. Bounded and short — the
    // single in-process consumer is blocked on the one envelope while it retries.
    private static readonly TimeSpan InlineRetryBackoff = TimeSpan.FromMilliseconds(200);

    public AgentCallIngestionWorker(
        IIngestionStream stream,
        IIngestionExecutor executor,
        MessagingConfiguration messaging,
        ILogger<AgentCallIngestionWorker> logger)
    {
        this.stream = stream;
        this.executor = executor;
        this.messaging = messaging;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Process envelopes concurrently. Each unit runs on its own async flow, so the AsyncLocal
        // ambient DbContext keeps every persist isolated — lifting throughput past the old
        // one-trace-at-a-time ceiling. Concurrency is bounded by configuration; the conversation /
        // version races this introduces are the same ones the multi-instance deployment already
        // tolerates (best-effort prior-call lookup + retryable unique-index recovery).
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, messaging.MaxConcurrency),
            CancellationToken = cancellationToken,
        };

        // Outer loop keeps the consumer alive across transport blips (e.g. a brief Redis outage):
        // ConsumeAsync may throw mid-stream, so we log, back off, and re-enter rather than letting
        // the BackgroundService fault permanently.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Parallel.ForEachAsync(
                    stream.ConsumeAsync(cancellationToken),
                    options,
                    async (envelope, ct) => await HandleAsync(envelope, ct));
            }
            catch (OperationCanceledException)
            {
                return; // graceful shutdown
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ingestion consumer loop failed; retrying shortly");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task HandleAsync(IngestEnvelope envelope, CancellationToken cancellationToken)
    {
        // Dedup guard (#261): skip an entry this worker is already processing — i.e. a reclaimed
        // duplicate that overlaps the still-in-flight original. The original owns the ack, so the
        // duplicate must neither process nor ack; just drop it.
        if (!inFlightEntries.TryAdd(envelope.MessageId, 0))
        {
            logger.LogDebug(
                "Skipping ingestion envelope {MessageId}: already in flight (reclaimed duplicate)",
                envelope.MessageId);
            return;
        }

        try
        {
            // A retryable failure is recovered differently per transport: one that redelivers unacked
            // entries (Redis) leaves the entry pending and tracks attempts across redeliveries; one
            // that does not (the in-process channel) must retry here and now, because an unacked
            // envelope is simply dropped — so deferring the retry would lose the captured call.
            await (stream.RedeliversUnacknowledged
                ? HandleWithRedeliveryAsync(envelope, cancellationToken)
                : HandleWithInlineRetryAsync(envelope, cancellationToken));
        }
        finally
        {
            // Clear only once the entry has left processing (acked, or left pending for a genuine
            // redelivery). A later genuine redelivery re-enters with the id cleared and is reprocessed
            // as intended; only an overlapping reclaim is skipped above.
            inFlightEntries.TryRemove(envelope.MessageId, out _);
        }
    }

    private async Task HandleWithRedeliveryAsync(IngestEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            await executor.IngestAsync(envelope.Message, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — leave the entry pending so it is reclaimed and reprocessed later.
            throw;
        }
        catch (EntityNotFoundException ex)
        {
            // Poison: the referenced provider/project no longer exists. Unrecoverable — ack to drop
            // it rather than redeliver forever.
            logger.LogWarning(ex, "Dropping ingestion envelope {MessageId}: referenced entity missing", envelope.MessageId);
            await AckAndForgetAsync(envelope.MessageId, cancellationToken);
            return;
        }
        catch (Exception ex)
        {
            // The processor swallows its own poison failures and rethrows only retryable storage
            // errors (transient DB outage, unique-index race). Leave the entry UNacked so Redis
            // redelivers it — but cap attempts so a deterministically-failing message can't loop.
            var attempts = failedAttempts.GetValueOrDefault(envelope.MessageId) + 1;
            if (attempts >= MaxRetryableAttempts)
            {
                logger.LogError(ex, "Dropping ingestion envelope {MessageId} after {Attempts} failed attempts", envelope.MessageId, attempts);
                await AckAndForgetAsync(envelope.MessageId, cancellationToken);
            }
            else
            {
                failedAttempts[envelope.MessageId] = attempts;
                logger.LogWarning(ex, "Retryable failure on ingestion envelope {MessageId} (attempt {Attempts}); leaving unacked for redelivery", envelope.MessageId, attempts);
            }
            return;
        }

        // Success — acknowledge and forget any prior attempt count.
        await AckAndForgetAsync(envelope.MessageId, cancellationToken);
    }

    // Non-redelivering transport (the in-process channel): an unacknowledged envelope is never
    // redelivered, so a retryable failure is retried inline — bounded attempts with a short backoff,
    // then dropped. No per-message state is kept, so this path cannot leak the failedAttempts
    // dictionary the way leaving it unacked-and-tracked would.
    private async Task HandleWithInlineRetryAsync(IngestEnvelope envelope, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetryableAttempts; attempt++)
        {
            try
            {
                await executor.IngestAsync(envelope.Message, cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // shutting down — abandon without dropping
            }
            catch (EntityNotFoundException ex)
            {
                // Poison: the referenced provider/project no longer exists. Not retryable — drop.
                logger.LogWarning(ex, "Dropping ingestion envelope {MessageId}: referenced entity missing", envelope.MessageId);
                return;
            }
            catch (Exception ex) when (attempt < MaxRetryableAttempts)
            {
                logger.LogWarning(ex, "Retryable failure on ingestion envelope {MessageId} (attempt {Attempt}/{Max}); retrying inline", envelope.MessageId, attempt, MaxRetryableAttempts);
                await Task.Delay(InlineRetryBackoff, cancellationToken);
            }
            catch (Exception ex)
            {
                // Exhausted: this transport cannot redeliver, so drop rather than let the exception
                // tear down the consumer loop.
                logger.LogError(ex, "Dropping ingestion envelope {MessageId} after {Attempts} inline attempts", envelope.MessageId, attempt);
                return;
            }
        }
    }

    // Test seam: ids currently tracked for cross-redelivery retry. Stays 0 on a non-redelivering
    // transport, which retries inline and keeps no per-message state — guards the leak fix.
    internal int TrackedRetryCount => failedAttempts.Count;

    // Test seam: ids currently being processed (the #261 dedup set). Settles back to 0 once every
    // in-flight envelope has been acked or left pending for redelivery.
    internal int InFlightCount => inFlightEntries.Count;

    private async Task AckAndForgetAsync(string messageId, CancellationToken cancellationToken)
    {
        failedAttempts.TryRemove(messageId, out _);
        await stream.AckAsync(messageId, cancellationToken);
    }
}
