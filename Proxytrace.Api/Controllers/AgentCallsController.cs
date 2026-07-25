using Proxytrace.Domain.Statistics;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Json;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Statistics;
using Proxytrace.Application.Statistics;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.Session;
using Proxytrace.Domain.Usage;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/agent-calls")]
public class AgentCallsController : ControllerBase
{
    private readonly IAgentCallRepository repository;
    private readonly IAgentRepository agentRepository;
    private readonly ISessionRepository sessionRepository;
    private readonly IDashboardStatistics statistics;
    private readonly ITraceBroadcaster traceBroadcaster;
    private readonly AgentCallDtoMapper agentCallDtoMapper;
    private readonly AgentDtoMapper agentDtoMapper;
    private readonly IAgentCall.CreateNew createCall;
    private readonly ICompletion.Create createCompletion;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    public AgentCallsController(
        IAgentCallRepository repository,
        IAgentRepository agentRepository,
        ISessionRepository sessionRepository,
        IDashboardStatistics statistics,
        ITraceBroadcaster traceBroadcaster,
        AgentCallDtoMapper agentCallDtoMapper,
        AgentDtoMapper agentDtoMapper,
        IAgentCall.CreateNew createCall,
        ICompletion.Create createCompletion,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.agentRepository = agentRepository;
        this.sessionRepository = sessionRepository;
        this.statistics = statistics;
        this.traceBroadcaster = traceBroadcaster;
        this.agentCallDtoMapper = agentCallDtoMapper;
        this.agentDtoMapper = agentDtoMapper;
        this.createCall = createCall;
        this.createCompletion = createCompletion;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    // Resolve the effective owning project of a list query and verify access. Admins
    // (accessible == null) may run any query. Non-admins must scope to a project they belong to —
    // directly via projectId or via the agent's project — otherwise the list returns nothing rather
    // than leaking other tenants' rows (#193).
    private async Task<bool> CanListAsync(Guid? projectId, Guid? agentId, CancellationToken cancellationToken)
    {
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);
        if (accessible is null)
            return true;
        if (projectId is { } pid)
            return accessible.Contains(pid);
        if (agentId is { } aid)
        {
            var agent = await agentRepository.FindAsync(aid, cancellationToken);
            return agent is not null && accessible.Contains(agent.Project.Id);
        }
        return false;
    }

    [HttpGet]
    public async Task<PagedResult<AgentCallListItemDto>> GetAll(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? endpointId = null,
        [FromQuery] string? model = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? httpStatus = null,
        [FromQuery] bool includeSystemAgents = true,
        [FromQuery] string? q = null,
        [FromQuery] Guid? conversationId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] bool outlierOnly = false,
        [FromQuery] OutlierFlags? anomalyFlags = null,
        [FromQuery] int? httpStatusClass = null,
        [FromQuery] ulong? minTokens = null,
        [FromQuery] ulong? maxTokens = null,
        [FromQuery] double? minLatencyMs = null,
        [FromQuery] double? maxLatencyMs = null,
        [FromQuery] string? toolName = null,
        [FromQuery] AgentCallSortField sortBy = AgentCallSortField.CreatedAt,
        [FromQuery] bool sortDesc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        if (!await CanListAsync(projectId, agentId, cancellationToken))
            return new PagedResult<AgentCallListItemDto>([], 0, page, pageSize);
        var filter = new AgentCallFilter(
            AgentId: agentId,
            ProjectId: projectId,
            EndpointId: endpointId,
            Model: model,
            From: from,
            To: to,
            HttpStatus: httpStatus,
            IncludeSystemAgents: includeSystemAgents,
            Query: q,
            ConversationId: conversationId,
            OutlierOnly: outlierOnly,
            AnomalyFlags: anomalyFlags,
            HttpStatusClass: httpStatusClass,
            MinTokens: minTokens,
            MaxTokens: maxTokens,
            MinLatencyMs: minLatencyMs,
            MaxLatencyMs: maxLatencyMs,
            ToolName: toolName,
            SortBy: sortBy,
            SortDescending: sortDesc,
            SessionId: sessionId);
        var (items, total) = await repository.GetFilteredListAsync(filter, page, pageSize, cancellationToken);
        return new PagedResult<AgentCallListItem>(items, total, page, pageSize).Map(agentCallDtoMapper.ToListItemDto);
    }

    /// <summary>
    /// Distinct tool names requested by any trace in the project — backs the traces filter's
    /// tool-name picker. When <paramref name="agentId"/> is supplied (an agent filter is active),
    /// the list is scoped to that agent's traces. Empty when the caller cannot access the project.
    /// </summary>
    [HttpGet("tool-names")]
    public async Task<IReadOnlyList<string>> GetToolNames(
        [FromQuery] Guid projectId,
        [FromQuery] Guid? agentId = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanListAsync(projectId, agentId, cancellationToken))
            return [];
        return await repository.GetToolNamesAsync(projectId, agentId, cancellationToken);
    }

    /// <summary>
    /// Full (fat) trace list — same filters as <see cref="GetAll"/> but each item carries the complete
    /// request/response/tools. Used only by bulk full-data flows that need the whole payload up front
    /// (suite-creation test-case building, playground replay), not the traces table. The table uses
    /// the light <see cref="GetAll"/>; individual selections use <see cref="Get"/>.
    /// </summary>
    [HttpGet("full")]
    public async Task<PagedResult<AgentCallDto>> GetAllFull(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? endpointId = null,
        [FromQuery] string? model = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? httpStatus = null,
        [FromQuery] bool includeSystemAgents = true,
        [FromQuery] string? q = null,
        [FromQuery] Guid? conversationId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        if (!await CanListAsync(projectId, agentId, cancellationToken))
            return new PagedResult<AgentCallDto>([], 0, page, pageSize);
        var filter = new AgentCallFilter(agentId, projectId, endpointId, model, from, to, httpStatus, includeSystemAgents, q, conversationId, SessionId: sessionId);
        var (items, total) = await repository.GetFilteredAsync(filter, page, pageSize, cancellationToken);
        return new PagedResult<IAgentCall>(items, total, page, pageSize).Map(agentCallDtoMapper.ToDto);
    }

    [HttpGet("overview")]
    public async Task<TracesOverviewDto> GetOverview(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] DateTimeOffset? from = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanListAsync(projectId, agentId, cancellationToken))
            return new TracesOverviewDto([], [], []);

        var latencyFilter = new StatisticsFilter(from, null, projectId, agentId);
        var breakdownFilter = new StatisticsFilter(from, null, projectId);

        // Scope the agent load to the project when filtered, instead of loading every agent and
        // discarding the rest in memory.
        Task<IReadOnlyList<IAgent>> agentsTask = projectId.HasValue
            ? agentRepository.GetByProjectAsync(projectId.Value, cancellationToken)
            : agentRepository.GetAllAsync(cancellationToken);
        Task<IReadOnlyDictionary<Guid, DateTimeOffset>> lastCallTask = repository.GetLastCallTimesAsync(cancellationToken);
        Task<IReadOnlyList<AgentBreakdownStat>> breakdownTask = statistics.GetAgentBreakdownAsync(breakdownFilter, cancellationToken);
        Task<IReadOnlyList<LatencyStat>> latencyTask = statistics.GetLatencyAsync(latencyFilter, cancellationToken);

        await Task.WhenAll(agentsTask, lastCallTask, breakdownTask, latencyTask);

        IReadOnlyDictionary<Guid, DateTimeOffset> lastCall = lastCallTask.Result;
        AgentListItemDto[] agents = agentsTask.Result
            .OrderByDescending(a => lastCall.TryGetValue(a.Id, out var t) ? t : DateTimeOffset.MinValue)
            .ThenByDescending(a => a.UpdatedAt)
            .Select(a => agentDtoMapper.ToListItemDto(a, lastCall.TryGetValue(a.Id, out var t) ? t : null))
            .ToArray();

        return new TracesOverviewDto(
            agents,
            breakdownTask.Result.Select(r => new AgentBreakdownDto(r.AgentId, r.CallCount)).ToArray(),
            latencyTask.Result.Select(r => new LatencyDto(r.EndpointId, r.P50Ms, r.P95Ms, r.P99Ms, r.MinMs, r.MaxMs, r.SampleCount)).ToArray());
    }

    [HttpGet("histogram")]
    public async Task<IReadOnlyList<TraceHistogramBucketDto>> GetHistogram(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? endpointId = null,
        [FromQuery] string? model = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int? httpStatus = null,
        [FromQuery] bool includeSystemAgents = true,
        [FromQuery] string? q = null,
        [FromQuery] Guid? conversationId = null,
        [FromQuery] Guid? sessionId = null,
        [FromQuery] bool outlierOnly = false,
        [FromQuery] OutlierFlags? anomalyFlags = null,
        [FromQuery] int? httpStatusClass = null,
        [FromQuery] ulong? minTokens = null,
        [FromQuery] ulong? maxTokens = null,
        [FromQuery] double? minLatencyMs = null,
        [FromQuery] double? maxLatencyMs = null,
        [FromQuery] string? toolName = null,
        [FromQuery] int buckets = 60,
        CancellationToken cancellationToken = default)
    {
        buckets = Math.Clamp(buckets, 1, 240);
        if (!await CanListAsync(projectId, agentId, cancellationToken))
            return [];
        // Same filter surface as GetAll (minus paging/sort — a histogram has neither), so the
        // timeline always reflects exactly the rows the filtered table shows.
        var filter = new AgentCallFilter(
            AgentId: agentId,
            ProjectId: projectId,
            EndpointId: endpointId,
            Model: model,
            From: from,
            To: to,
            HttpStatus: httpStatus,
            IncludeSystemAgents: includeSystemAgents,
            Query: q,
            ConversationId: conversationId,
            OutlierOnly: outlierOnly,
            AnomalyFlags: anomalyFlags,
            HttpStatusClass: httpStatusClass,
            MinTokens: minTokens,
            MaxTokens: maxTokens,
            MinLatencyMs: minLatencyMs,
            MaxLatencyMs: maxLatencyMs,
            ToolName: toolName,
            SessionId: sessionId);
        var result = await repository.GetHistogramAsync(filter, buckets, cancellationToken);
        return result.Select(b => new TraceHistogramBucketDto(b.Start, b.Total, b.Errors)).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AgentCallDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var call = await repository.FindAsync(id, cancellationToken);
        if (call is null)
            return NotFound();
        // Hide other tenants' traces behind a 404 rather than disclosing the request/response.
        if (!await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
            return NotFound();
        return agentCallDtoMapper.ToDto(call);
    }

    /// <summary>
    /// Test-only: seeds an agent call (trace) directly, bypassing the ingestion pipeline so the
    /// e2e suite can create traces without making real LLM calls. The call is recorded against the
    /// resolved agent's current version and endpoint, with an HTTP 200 status and a "stop" finish
    /// reason.
    /// </summary>
    [HttpPost("seed")]
    [TestOnlyEndpoint]
    public async Task<ActionResult<AgentCallDto>> Seed(
        [FromBody] SeedAgentCallRequest request,
        CancellationToken cancellationToken)
    {
        IAgent? agent = await agentRepository.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return NotFound();

        var conversation = Conversation.Create();
        if (!string.IsNullOrEmpty(request.SystemContent))
            conversation = conversation.WithSystemMessage(new SystemMessage([Proxytrace.Domain.Message.Content.FromText(request.SystemContent)]));
        conversation = conversation.With(new UserMessage([Proxytrace.Domain.Message.Content.FromText(request.UserContent)]));

        var assistantMessage = new AssistantMessage(
            [Proxytrace.Domain.Message.Content.FromText(request.AssistantContent)],
            (request.ToolNames ?? []).Select((name, i) => new ToolRequest($"seed-{i}", name, "{}")).ToList());
        var usage = new TokenUsage((ulong)request.InputTokens, (ulong)request.OutputTokens);
        ICompletion completion = createCompletion(
            assistantMessage,
            usage,
            TimeSpan.FromMilliseconds(request.DurationMs));

        // Mirror ingestion's session handling: when a session key is sent, derive its deterministic
        // id, stamp it on the call, and bump the denormalized session counters — so e2e/dev can create
        // sessioned traces (and exercise the session list / filter) without the ingestion proxy.
        Guid projectId = agent.Project.Id;
        string? sessionKey = string.IsNullOrWhiteSpace(request.SessionKey)
            ? null
            : SessionIdDerivation.TruncateKey(request.SessionKey);
        Guid? sessionId = sessionKey is null ? null : SessionIdDerivation.Derive(projectId, sessionKey);

        IAgentCall call = await repository.AddAsync(
            createCall(
                agent: agent,
                version: agent.CurrentVersion,
                endpoint: agent.Endpoint,
                request: conversation,
                response: completion,
                httpStatus: HttpStatusCode.OK,
                finishReason: "stop",
                errorMessage: null,
                modelParameters: agent.ModelParameters,
                conversationId: request.ConversationId,
                sessionId: sessionId,
                outlierFlags: (OutlierFlags)(request.OutlierFlags ?? 0)),
            cancellationToken);

        if (sessionId is { } sid && sessionKey is not null)
        {
            await sessionRepository.RecordActivityAsync(
                sid, sessionKey, projectId,
                totalTokens: request.InputTokens + request.OutputTokens,
                lastActivityAt: call.CreatedAt,
                cancellationToken);
        }

        // Publish to the trace SSE broadcaster exactly as the ingestion pipeline does, so
        // dashboard/traces SSE clients receive the seeded trace.
        traceBroadcaster.Publish(TraceCreatedEvent.Create(call));

        return Ok(agentCallDtoMapper.ToDto(call));
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // Snapshot the caller's project scope once; non-admins only receive traces for projects they
        // belong to (admins: accessible == null → all). Without this the stream broadcast every
        // tenant's traces to any authenticated user.
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);

        var reader = traceBroadcaster.Subscribe(cancellationToken);

        // Route through the heartbeat reader so a quiet stream periodically writes a comment frame;
        // a half-open socket (which never raises RequestAborted) then fails the write and the
        // broadcaster's cancellation registration unsubscribes, instead of leaking the slot forever.
        await foreach (var evt in SseWriter.ReadWithHeartbeatAsync(reader, cancellationToken))
        {
            if (evt is null)
            {
                await SseWriter.WriteHeartbeatAsync(Response, cancellationToken);
                continue;
            }

            if (accessible is not null && !accessible.Contains(evt.ProjectId))
                continue;
            var data = SseEventSerializer.Serialize(evt);
            await Response.WriteAsync($"event: trace-created\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var call = await repository.FindAsync(id, cancellationToken);
        if (call is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
            return NotFound();
        var removed = await repository.RemoveAsync(id, cancellationToken);
        if (removed)
        {
            audit.LogAudit(
                AuditAction.AgentCallDeleted, nameof(IAgentCall), id, call.Agent.Name,
                projectId: call.Agent.Project.Id);
        }

        return removed ? NoContent() : NotFound();
    }
}
