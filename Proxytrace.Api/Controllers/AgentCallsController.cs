using Proxytrace.Domain.Statistics;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Json;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Statistics;
using Proxytrace.Api.Dto.TestCases;
using Proxytrace.Application.Statistics;
using Proxytrace.Application.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Session;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for agent calls operations.
/// </summary>
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
    private readonly ITestSuiteRepository suiteRepository;
    private readonly ITestCaseSynthesisService synthesis;
    private readonly TestCaseProposalDtoMapper proposalMapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallsController"/> class.
    /// </summary>
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
        ILogger<Audit> audit,
        ITestSuiteRepository suiteRepository,
        ITestCaseSynthesisService synthesis,
        TestCaseProposalDtoMapper proposalMapper)
    {
        this.suiteRepository = suiteRepository;
        this.synthesis = synthesis;
        this.proposalMapper = proposalMapper;
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

    // Resolve the projects a list query may read. Admins (scope == null) may run any query;
    // everyone else is confined to the projects they belong to, so no query can leak another
    // tenant's rows (#193). An unfiltered request is scoped to the caller's own projects rather
    // than answered with an empty page (#482). An agentId is authorized against the same scope —
    // the query then filters by that agent, which is already confined to one project.
    private async Task<IReadOnlyCollection<Guid>?> ListScopeAsync(
        Guid? projectId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope is null || scope.IsEmpty() || agentId is not { } aid)
            return scope;

        var agent = await agentRepository.FindAsync(aid, cancellationToken);
        return agent is not null && scope.Contains(agent.Project.Id) ? scope : [];
    }

    // The agents the overview lists: one project's when the scope names one (the indexed load),
    // the union of the caller's projects when it spans several, and every agent for an unrestricted
    // admin. Mirrors EvaluatorsController.ListScopedAsync — the agents table is small and bounded by
    // the licensed agent limit, so narrowing a multi-project scope in memory is cheap.
    private async Task<IReadOnlyList<IAgent>> ScopedAgentsAsync(
        IReadOnlyCollection<Guid>? scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsEmpty())
            return [];
        if (scope.SingleProject() is { } projectId)
            return await agentRepository.GetByProjectAsync(projectId, cancellationToken);
        var all = await agentRepository.GetAllAsync(cancellationToken);
        return scope is null ? all : all.Where(a => scope.Contains(a.Project.Id)).ToArray();
    }

    // Truncate a caller-supplied session key and pair it with its derived id, so the seed endpoint
    // carries both as one value and never has to re-check the key for null.
    private static (Guid Id, string Key) DeriveSession(Guid projectId, string sessionKey)
    {
        string key = SessionIdDerivation.TruncateKey(sessionKey);
        return (SessionIdDerivation.Derive(projectId, key), key);
    }

    /// <summary>
    /// Gets the all.
    /// </summary>
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
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<AgentCallListItemDto>([], 0, page, pageSize);
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();
        var filter = new AgentCallFilter(
            AgentId: agentId,
            ProjectId: scopedProjectId,
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
            SessionId: sessionId,
            ProjectIds: scopedProjectIds);
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
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
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
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<AgentCallDto>([], 0, page, pageSize);
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();
        var filter = new AgentCallFilter(agentId, scopedProjectId, endpointId, model, from, to, httpStatus, includeSystemAgents, q, conversationId, SessionId: sessionId, ProjectIds: scopedProjectIds);
        var (items, total) = await repository.GetFilteredAsync(filter, page, pageSize, cancellationToken);
        return new PagedResult<IAgentCall>(items, total, page, pageSize).Map(agentCallDtoMapper.ToDto);
    }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    [HttpGet("overview")]
    public async Task<TracesOverviewDto> GetOverview(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] DateTimeOffset? from = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return new TracesOverviewDto([], [], []);

        // A scope naming exactly one project keeps the single-project filter, so the common case —
        // the web UI, which always sends a projectId, and a REST API key, confined to one project —
        // runs the unchanged indexed by-one-project aggregate. A caller who may read several and
        // named none aggregates over that set instead of getting an empty overview (#483).
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();
        var latencyFilter = new StatisticsFilter(from, null, scopedProjectId, agentId, ProjectIds: scopedProjectIds);
        var breakdownFilter = new StatisticsFilter(from, null, scopedProjectId, ProjectIds: scopedProjectIds);

        Task<IReadOnlyList<IAgent>> agentsTask = ScopedAgentsAsync(scope, cancellationToken);
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

    /// <summary>
    /// Gets the histogram.
    /// </summary>
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
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return [];
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();
        // Same filter surface as GetAll (minus paging/sort — a histogram has neither), so the
        // timeline always reflects exactly the rows the filtered table shows.
        var filter = new AgentCallFilter(
            AgentId: agentId,
            ProjectId: scopedProjectId,
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
            SessionId: sessionId,
            ProjectIds: scopedProjectIds);
        var result = await repository.GetHistogramAsync(filter, buckets, cancellationToken);
        return result.Select(b => new TraceHistogramBucketDto(b.Start, b.Total, b.Errors)).ToList();
    }

    /// <summary>
    /// Aggregate over every trace matching the filter — the traces KPI band. Deliberately unpaged:
    /// the trace list scrolls rather than pages, so its KPIs describe the whole filtered set.
    /// Takes the same filter parameters as <see cref="GetAll"/> so the band and the table can never
    /// describe different sets; paging and sorting are omitted because neither affects an aggregate.
    /// </summary>
    [HttpGet("summary")]
    public async Task<AgentCallSummaryDto> GetSummary(
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
        CancellationToken cancellationToken = default)
    {
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return agentCallDtoMapper.ToSummaryDto(AgentCallSummary.Empty);
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();

        var filter = new AgentCallFilter(
            AgentId: agentId,
            ProjectId: scopedProjectId,
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
            SessionId: sessionId,
            ProjectIds: scopedProjectIds);

        return agentCallDtoMapper.ToSummaryDto(await repository.GetSummaryAsync(filter, cancellationToken));
    }

    /// <summary>
    /// Gets.
    /// </summary>
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
    /// Proposes the test cases worth building from this trace's whole conversation. Read-only —
    /// nothing is created; the caller approves a subset and writes them through the test-suite
    /// endpoints. Pass <c>suiteId</c> so the agent can judge whether that suite's evaluators can
    /// score what it proposes, and <c>rounds</c> to refine a previous answer instead of starting over.
    /// </summary>
    [HttpPost("{id:guid}/test-case-proposals")]
    [RequiresFeature(LicenseFeature.TestCaseSynthesis)]
    public async Task<ActionResult<TestCaseProposalSetDto>> ProposeTestCases(
        Guid id,
        [FromBody] SynthesizeTestCasesRequest request,
        CancellationToken cancellationToken)
    {
        var call = await repository.FindAsync(id, cancellationToken);
        if (call is null)
            return NotFound();
        // Hide other tenants' traces behind a 404 rather than disclosing that the id exists.
        if (!await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
            return NotFound();

        ITestSuite? destination = null;
        if (request.SuiteId is { } suiteId)
        {
            destination = await suiteRepository.FindAsync(suiteId, cancellationToken);
            if (destination is not null
                && !await accessGuard.CanAccessProjectAsync(destination.Agent.Project.Id, cancellationToken))
            {
                destination = null;
            }
        }

        // Keep only the most recent rounds: the model needs the recent exchange, and an unbounded
        // history posted by a client would grow the prompt without bound.
        IReadOnlyList<SynthesisRound> rounds =
        [
            .. (request.Rounds ?? [])
                .TakeLast(TestCaseProposalSet.MaxRounds)
                .Select(proposalMapper.ToDomain),
        ];

        TestCaseProposalSet proposals = await synthesis.SynthesizeAsync(
            call, destination, rounds, request.Instruction, cancellationToken);

        return proposalMapper.ToDto(proposals);
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
            conversation = conversation.WithSystemMessage(new SystemMessage([Nordstein.Core.AI.Messages.Content.FromText(request.SystemContent)]));
        conversation = conversation.With(new UserMessage([Nordstein.Core.AI.Messages.Content.FromText(request.UserContent)]));

        var assistantMessage = new AssistantMessage(
            [Nordstein.Core.AI.Messages.Content.FromText(request.AssistantContent)],
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
        (Guid Id, string Key)? session = string.IsNullOrWhiteSpace(request.SessionKey)
            ? null
            : DeriveSession(projectId, request.SessionKey);

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
                sessionId: session?.Id,
                outlierFlags: (OutlierFlags)(request.OutlierFlags ?? 0)),
            cancellationToken);

        if (session is { } stamped)
        {
            await sessionRepository.RecordActivityAsync(
                stamped.Id, stamped.Key, projectId,
                totalTokens: request.InputTokens + request.OutputTokens,
                lastActivityAt: call.CreatedAt,
                cancellationToken);
        }

        // Publish to the trace SSE broadcaster exactly as the ingestion pipeline does, so
        // dashboard/traces SSE clients receive the seeded trace.
        traceBroadcaster.Publish(TraceCreatedEvent.Create(call));

        return Ok(agentCallDtoMapper.ToDto(call));
    }

    /// <summary>
    /// Stream.
    /// </summary>
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

    /// <summary>
    /// Deletes.
    /// </summary>
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

            await ReverseSessionActivityAsync(call, cancellationToken);
        }

        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// Gives back the counters this trace contributed to its session when it was ingested. Mirrors
    /// the bump in <c>AgentCallProcessor</c> — including its best-effort stance: the trace row is
    /// already gone, so a failure here must not turn a successful delete into an error response.
    /// The token total is computed exactly as the denormalized column is, so the reversal is exact.
    /// </summary>
    private async Task ReverseSessionActivityAsync(IAgentCall call, CancellationToken cancellationToken)
    {
        if (call.SessionId is not { } sessionId)
            return;

        try
        {
            long totalTokens = call.Response?.Usage is { } usage
                ? (long)(usage.InputTokenCount + usage.OutputTokenCount)
                : 0;
            await sessionRepository.RecordTraceRemovalsAsync(
                [new SessionTraceRemoval(sessionId, TraceCount: 1, TotalTokens: totalTokens)],
                cancellationToken);
        }
        catch (Exception ex)
        {
            audit.LogWarning(ex, "Session counter reversal failed for session {SessionId}", sessionId);
        }
    }
}
