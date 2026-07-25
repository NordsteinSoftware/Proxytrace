using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.CustomAnomaly;
using Proxytrace.Application.CustomAnomaly.Internal;
using Proxytrace.Application.Outliers;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Session;
using Proxytrace.Licensing;

namespace Proxytrace.Application.Ingestion.Internal;

internal sealed class AgentCallProcessor : IAgentCallProcessor
{
    private readonly IAgentCallRepository agentCallRepository;
    private readonly IAgentCall.CreateNew createNewCall;
    private readonly IPromptTemplate.Create createPromptTemplate;
    private readonly IOpenAiCallParser parser;
    private readonly IAgentRepository agentRepository;
    private readonly IAgentVersionRepository versionRepository;
    private readonly IAgentVersionMatcher matcher;
    private readonly ITraceBroadcaster traceBroadcaster;
    private readonly ILicenseService license;
    private readonly IOutlierDetector outlierDetector;
    private readonly ICustomAnomalyReviewQueue anomalyReviewQueue;
    private readonly IBlockedCallRecorder blockedCallRecorder;
    private readonly ISessionRepository sessionRepository;
    private readonly ILogger<AgentCallProcessor> logger;

    public AgentCallProcessor(
        IAgentCallRepository agentCallRepository,
        IAgentCall.CreateNew createNewCall,
        IPromptTemplate.Create createPromptTemplate,
        IOpenAiCallParser parser,
        IAgentRepository agentRepository,
        IAgentVersionRepository versionRepository,
        IAgentVersionMatcher matcher,
        ITraceBroadcaster traceBroadcaster,
        ILicenseService license,
        IOutlierDetector outlierDetector,
        ICustomAnomalyReviewQueue anomalyReviewQueue,
        IBlockedCallRecorder blockedCallRecorder,
        ISessionRepository sessionRepository,
        ILogger<AgentCallProcessor> logger)
    {
        this.agentCallRepository = agentCallRepository;
        this.createNewCall = createNewCall;
        this.createPromptTemplate = createPromptTemplate;
        this.parser = parser;
        this.agentRepository = agentRepository;
        this.versionRepository = versionRepository;
        this.matcher = matcher;
        this.traceBroadcaster = traceBroadcaster;
        this.license = license;
        this.outlierDetector = outlierDetector;
        this.anomalyReviewQueue = anomalyReviewQueue;
        this.blockedCallRecorder = blockedCallRecorder;
        this.sessionRepository = sessionRepository;
        this.logger = logger;
    }

    public async Task IngestAsync(
        IngestJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            var parsed = await parser.TryParse(
                job.Provider,
                job.RequestBody,
                job.ResponseBody,
                job.Duration,
                job.HttpStatus,
                cancellationToken);

            if (parsed == null)
            {
                return;
            }

            // ── Resolve conversation context ───────────────────────────────────
            var (conversationId, priorConversationCall) = await ResolveConversationAsync(
                job, cancellationToken);

            // ── Resolve version ────────────────────────────────────────────────
            var promptTemplate = createPromptTemplate("unknown", parsed.SystemMessage.ToString());

            IAgentVersion? version;
            if (priorConversationCall is not null
                && parsed.Tools.Count == 0
                && string.Equals(
                    priorConversationCall.Version.SystemPrompt.Template,
                    promptTemplate.Template,
                    StringComparison.Ordinal))
            {
                // Continuation call without re-sent tools *and* identical system prompt: inherit
                // prior call's version verbatim.
                version = priorConversationCall.Version;
            }
            else if (job.AgentName is { Length: > 0 } agentName)
            {
                // Explicit attribution: the caller named the owning agent (same-origin client like
                // Tracey, or the X-Proxytrace-Agent header). Attribute directly, skipping the
                // prompt/tool similarity matcher entirely.
                version = await ResolveVersionForNamedAgentAsync(job, agentName, promptTemplate, parsed, cancellationToken);
            }
            else
            {
                version = await ResolveVersionAsync(job, promptTemplate, parsed, cancellationToken);
            }

            if (version is null)
            {
                // The licensed agent limit was reached and this trace belongs to a new agent; drop it.
                return;
            }

            var agent = await version.GetAgentAsync(cancellationToken);

            if (agent.Endpoint.Id != parsed.Endpoint.Id)
            {
                agent = await agent.ChangeEndpoint(parsed.Endpoint, cancellationToken);
            }

            if (!agent.ModelParameters.Equals(parsed.ModelParameters))
            {
                agent = await agent.ChangeModelParameters(parsed.ModelParameters, cancellationToken);
            }

            // A proxy-blocked call never reached the provider: it carries no usable metrics for the
            // statistical baseline, so it is flagged Blocked instead of outlier-evaluated.
            var outlierFlags = job.BlockedByDetectorId is not null
                ? OutlierFlags.Blocked
                : await DetectOutliersAsync(
                    agent, parsed.Response, isTurn2Plus: priorConversationCall is not null, cancellationToken);

            // Resolve the debugging session from the (truncated) session key. Deriving the id from the
            // *truncated* key — the same key stored on the row — keeps the stamp and the row consistent.
            (Guid Id, string Key)? session = null;
            if (!string.IsNullOrWhiteSpace(job.SessionId))
            {
                var key = SessionIdDerivation.TruncateKey(job.SessionId);
                session = (SessionIdDerivation.Derive(job.Project.Id, key), key);
            }

            var call = createNewCall(
                agent: agent,
                version: version,
                endpoint: parsed.Endpoint,
                request: parsed.Request,
                response: parsed.Response,
                httpStatus: parsed.HttpStatus,
                finishReason: parsed.FinishReason,
                errorMessage: parsed.ErrorMessage,
                modelParameters: parsed.ModelParameters,
                conversationId: conversationId,
                sessionId: session?.Id,
                outlierFlags: outlierFlags);

            call = await agentCallRepository.AddAsync(call, cancellationToken);

            // Upsert the session AFTER the call persists: the trace is the source of truth, so this is
            // best-effort — a failure here logs and is swallowed, never failing or duplicating ingestion
            // (the call row already exists). TotalTokens mirrors the value AgentCallConfig denormalizes
            // onto AgentCallEntity.TotalTokens.
            if (session is { } s)
            {
                try
                {
                    var totalTokens = call.Response?.Usage is { } u
                        ? (long)(u.InputTokenCount + u.OutputTokenCount)
                        : 0;
                    await sessionRepository.RecordActivityAsync(
                        s.Id, s.Key, job.Project.Id, totalTokens, call.CreatedAt, cancellationToken);
                }
                catch (Exception e)
                {
                    // Deliberately also swallows OperationCanceledException: the call row is already
                    // committed, so letting a shutdown-time cancellation escape here would fail the
                    // ingest post-persist and make a redelivering transport ingest the trace a second
                    // time. The session counters are best-effort; the trace is not.
                    logger.LogWarning(e, "Session activity upsert failed for session {SessionId}", s.Id);
                }
            }

            traceBroadcaster.Publish(TraceCreatedEvent.Create(call));

            if (job is { BlockedByDetectorId: { } blockedBy, BlockedDetectorName: { } blockedName, BlockedTriggerPattern: { } blockedPattern })
            {
                // Proxy-blocked: record attribution + SSE + notification instead of enqueueing the
                // LLM review — there is no provider response to judge.
                await blockedCallRecorder.RecordAsync(
                    call, blockedBy, blockedName, blockedPattern, cancellationToken);
            }
            else if (!agent.IsSystemAgent)
            {
                // Queue the persisted call for custom-anomaly review — a cheap in-process channel
                // write (the LLM review runs asynchronously in the background worker). System agents'
                // traffic (evaluator judges, Tracey) is internal plumbing and is not reviewed.
                await anomalyReviewQueue.EnqueueAsync(call.Id, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            // Concurrency races (unique-index trip on version fingerprint / version number) and
            // transient DB failures are retryable — rethrow so the messaging worker requeues.
            logger.LogWarning(ex, "Retryable storage error ingesting agent call (provider={Provider}, status={HttpStatus})",
                job.Provider.Name, job.HttpStatus);
            throw;
        }
        catch (Exception ex)
        {
            // Poison: malformed payload, validation failure, missing referenced entity. Drop.
            logger.LogWarning(ex, "Failed to ingest agent call (provider={Provider}, status={HttpStatus})",
                job.Provider.Name,
                job.HttpStatus);
        }
    }

    /// <summary>
    /// Retryable = anything the messaging worker should requeue. Includes EF Core's
    /// <c>DbUpdateException</c> (unique-index races, optimistic concurrency) and ADO.NET
    /// <c>DbException</c> (transient connectivity, deadlocks). Identified by type name to avoid
    /// taking a hard EF reference in the application layer.
    /// </summary>
    private static bool IsRetryable(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            string name = e.GetType().Name;
            if (name is "DbUpdateException" or "DbUpdateConcurrencyException")
            {
                return true;
            }
            if (e is DbException)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Resolves the version for a call whose owning agent was named explicitly. Bypasses the
    /// similarity matcher: the named agent is looked up (created from the wire if it is the first
    /// call for that name), and a version with an identical strict fingerprint is reused — otherwise
    /// a new version is appended. The version content always comes from the actual request, so the
    /// backend never has to mirror the client's tool schemas or system prompt.
    /// </summary>
    private async Task<IAgentVersion?> ResolveVersionForNamedAgentAsync(
        IngestJob job,
        string agentName,
        IPromptTemplate promptTemplate,
        ParseResult parsed,
        CancellationToken cancellationToken)
    {
        var agent = await agentRepository.FindByNameAsync(job.Project, agentName, cancellationToken);
        if (agent is null)
        {
            // First call for this name: create the agent + v1 straight from the wire.
            var namedPrompt = createPromptTemplate(agentName, parsed.SystemMessage.ToString());
            var created = await agentRepository.CreateWithInitialVersionAsync(
                agentName,
                namedPrompt,
                parsed.Tools,
                job.Project,
                parsed.Endpoint,
                parsed.ModelParameters,
                isSystemAgent: false,
                cancellationToken);
            return created.CurrentVersion;
        }

        // Existing named agent: reuse a version with an identical strict fingerprint, else append one.
        var targetFingerprint = versionRepository.GetStrictFingerprint(promptTemplate, parsed.Tools);
        var versions = await versionRepository.GetByAgentAsync(agent, cancellationToken);
        var match = versions.FirstOrDefault(
            v => versionRepository.GetStrictFingerprint(v.SystemPrompt, v.Tools) == targetFingerprint);
        if (match is not null)
        {
            return match;
        }

        var updated = await agent.CreateNewVersionAsync(promptTemplate, parsed.Tools, cancellationToken);
        return updated.CurrentVersion;
    }

    private async Task<IAgentVersion?> ResolveVersionAsync(
        IngestJob job,
        IPromptTemplate promptTemplate,
        ParseResult parsed,
        CancellationToken cancellationToken)
    {
        // 1. Strict fingerprint hit -> reuse existing version.
        var strictHit = await versionRepository.FindByStrictFingerprintAsync(
            job.Project, promptTemplate, parsed.Tools, cancellationToken);
        if (strictHit is not null)
        {
            return strictHit;
        }

        // 2. Fuzzy match -> new version under the matched agent.
        var similar = await matcher.FindSimilarVersionAsync(
            job.Project, promptTemplate, parsed.Tools, cancellationToken);
        if (similar is not null)
        {
            var similarAgent = await similar.GetAgentAsync(cancellationToken);
            var updatedAgent = await similarAgent.CreateNewVersionAsync(
                promptTemplate, parsed.Tools, cancellationToken);
            return updatedAgent.CurrentVersion;
        }

        // 3. No similar agent -> would create a brand-new agent. Enforce the licensed agent cap
        // first. Ingestion is async (the proxied client response has already been returned), so
        // there is no request left to reject with 402 — over the limit we drop the trace.
        var maxAgents = license.GetLimit(LicenseLimit.MaxAgents);
        if (maxAgents != long.MaxValue)
        {
            var existingAgents = await agentRepository.CountNonSystemAsync(cancellationToken);
            if (existingAgents >= maxAgents)
            {
                logger.LogWarning(
                    "Agent limit reached ({Existing}/{Max}); dropping trace for a new agent",
                    existingAgents, maxAgents);
                return null;
            }
        }

        // No similar agent -> brand-new agent + v1 (delegates to repository). We already
        // performed the strict-fingerprint lookup above; skip the redundant pre-check inside the
        // repository while still benefiting from its fingerprint-keyed lock + post-write race
        // recovery on the unique index.
        var newAgent = await agentRepository.GetOrCreateAsync(
            promptTemplate,
            parsed.Tools,
            job.Project,
            parsed.Endpoint,
            modelParameters: parsed.ModelParameters,
            skipStrictPreCheck: true,
            cancellationToken: cancellationToken);

        return newAgent.CurrentVersion;
    }

    /// <summary>
    /// Flags the call's outlier characteristics against the agent's recent baseline. Only successful
    /// calls (with a response) are evaluated — errors carry no usable token/latency metrics and are
    /// excluded from the baseline too. Cache-hit is only meaningful from turn 2 onward, so it is passed
    /// as <see langword="null"/> for the first turn of a conversation.
    /// </summary>
    private async Task<OutlierFlags> DetectOutliersAsync(
        IAgent agent,
        ICompletion? response,
        bool isTurn2Plus,
        CancellationToken cancellationToken)
    {
        if (response is null)
        {
            return OutlierFlags.None;
        }

        ulong inputTokens = response.Usage?.InputTokenCount ?? 0UL;
        ulong outputTokens = response.Usage?.OutputTokenCount ?? 0UL;
        ulong cachedInputTokens = response.Usage?.CachedInputTokenCount ?? 0UL;

        double? cacheHitRate = isTurn2Plus && inputTokens > 0UL
            ? Math.Clamp(cachedInputTokens / (double)inputTokens, 0d, 1d)
            : null;

        int toolCalls = response.Response.ToolRequests.Count;

        var metrics = new OutlierMetrics(
            TotalTokens: inputTokens + outputTokens,
            LatencyMs: response.Latency.TotalMilliseconds,
            CacheHitRate: cacheHitRate,
            ToolCalls: toolCalls);

        return await outlierDetector.EvaluateAsync(agent.Id, metrics, cancellationToken);
    }

    private async Task<(Guid? conversationId, IAgentCall? priorCall)> ResolveConversationAsync(
        IngestJob job,
        CancellationToken cancellationToken)
    {
        // Explicit thread key wins; fall back to the session key so clients from before the
        // session/conversation split keep byte-identical conversation grouping.
        var conversationKey = job.ConversationId ?? job.SessionId;
        if (conversationKey is null)
        {
            return (null, null);
        }

        var conversationGuid = ParseCorrelationKey(conversationKey);
        var prior = await agentCallRepository
            .FindLatestByConversationIdAsync(conversationGuid, job.Project, cancellationToken);
        return (conversationGuid, prior);
    }

    private static Guid ParseCorrelationKey(string key)
    {
        if (Guid.TryParse(key, out var guid))
        {
            return guid;
        }

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
