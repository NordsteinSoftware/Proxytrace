using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Api.Json;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Paging;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/test-run-groups")]
public class TestRunGroupsController : ControllerBase
{
    private readonly ITestRunGroupRepository groupRepository;
    private readonly ITestRunRepository runRepository;
    private readonly ITestSuiteRepository suiteRepository;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly ITestRunnerService runner;
    private readonly ITestResultBroadcaster broadcaster;
    private readonly IOptimizerService optimizerService;
    private readonly TestRunDtoMapper runMapper;
    private readonly IAgentRepository agentRepository;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    public TestRunGroupsController(
        ITestRunGroupRepository groupRepository,
        ITestRunRepository runRepository,
        ITestSuiteRepository suiteRepository,
        IRepository<IModelEndpoint> endpoints,
        ITestRunnerService runner,
        ITestResultBroadcaster broadcaster,
        IOptimizerService optimizerService,
        TestRunDtoMapper runMapper,
        IAgentRepository agentRepository,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.groupRepository = groupRepository;
        this.runRepository = runRepository;
        this.suiteRepository = suiteRepository;
        this.endpoints = endpoints;
        this.runner = runner;
        this.broadcaster = broadcaster;
        this.optimizerService = optimizerService;
        this.runMapper = runMapper;
        this.agentRepository = agentRepository;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    // Resolve the effective owning project of a list query and verify access. Admins
    // (accessible == null) pass for any scope. Non-admins must scope to a project they belong to —
    // via projectId, the suite's project, or the agent's project — otherwise the query returns
    // nothing rather than leaking other tenants' rows.
    // The projects this list request may read: null for an admin, the caller's own projects when no
    // narrower filter is given (#482), and [] when a named suite/agent/project is out of reach.
    private async Task<IReadOnlyCollection<Guid>?> ListScopeAsync(
        Guid? suiteId,
        Guid? agentId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope is null || scope.IsEmpty())
            return scope;

        if (suiteId is { } sid)
        {
            var suite = await suiteRepository.FindAsync(sid, cancellationToken);
            return suite is not null && scope.Contains(suite.Agent.Project.Id) ? scope : [];
        }

        if (agentId is { } aid)
        {
            var agent = await agentRepository.FindAsync(aid, cancellationToken);
            return agent is not null && scope.Contains(agent.Project.Id) ? scope : [];
        }

        return scope;
    }

    [HttpGet]
    public async Task<PagedResult<TestRunGroupListItemDto>> GetAll(
        [FromQuery] Guid? suiteId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] bool includeSystem = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scope = await ListScopeAsync(suiteId, agentId, projectId, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<TestRunGroupListItemDto>([], 0, page, pageSize);

        PagedResult<ITestRunGroup> paged;
        if (suiteId.HasValue)
            paged = await groupRepository.GetBySuitePagedAsync(suiteId.Value, page, pageSize, includeSystem, cancellationToken);
        else if (agentId.HasValue)
            paged = await groupRepository.GetByAgentPagedAsync(agentId.Value, page, pageSize, includeSystem, cancellationToken);
        else if (scope is null)
            paged = await groupRepository.GetPagedAsync(page, pageSize, cancellationToken);
        else
            paged = await groupRepository.GetByProjectsPagedAsync(scope, page, pageSize, includeSystem, cancellationToken);

        var items = await Task.WhenAll(
            paged.Items.Select(g => runMapper.ToListItemDtoAsync(g, runRepository, cancellationToken)));
        return new PagedResult<TestRunGroupListItemDto>(items, paged.Total, paged.Page, paged.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestRunGroupDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var group = await groupRepository.FindAsync(id, cancellationToken);
        if (group is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        return await ToDtoAsync(group, cancellationToken);
    }

    [HttpPost]
    public async Task<ActionResult<TestRunGroupDto>> Create(
        [FromBody] CreateTestRunGroupRequest request,
        CancellationToken cancellationToken)
    {
        var suite = await suiteRepository.FindAsync(request.TestSuiteId, cancellationToken);
        if (suite is null)
            return BadRequest($"Test suite {request.TestSuiteId} not found.");

        if (!await accessGuard.CanAccessProjectAsync(suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        if (request.ModelEndpointIds.Count == 0)
            return BadRequest("At least one endpoint must be specified.");

        if (request.ModelEndpointIds.Count > ITestRunGroup.MaxModelEndpoints)
            return BadRequest($"A test suite can be run against at most {ITestRunGroup.MaxModelEndpoints} model endpoints.");

        if (request.SampleCount is < 1 or > ITestRunGroup.MaxSampleCount)
            return BadRequest($"Sample count must be between 1 and {ITestRunGroup.MaxSampleCount}.");

        var endpointList = await Task.WhenAll(
            request.ModelEndpointIds.Select(id => endpoints.GetAsync(id, cancellationToken)));

        var group = await runner.RunInBackgroundAsync(
            suite, endpointList, sampleCount: request.SampleCount, cancellationToken: cancellationToken);

        var projectId = await agentRepository.GetProjectIdAsync(suite.Agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestRunStarted, nameof(ITestRunGroup), group.Id, suite.Name, projectId: projectId);

        return AcceptedAtAction(nameof(Get), new { id = group.Id }, await ToDtoAsync(group, cancellationToken));
    }

    [HttpGet("{id:guid}/stream")]
    public async Task Stream(Guid id, CancellationToken cancellationToken)
    {
        var group = await groupRepository.FindAsync(id, cancellationToken);
        if (group is null || !await accessGuard.CanAccessProjectAsync(group.Suite.Agent.Project.Id, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // Linked token so every exit path (already-terminal early return, completion, disconnect)
        // cancels the subscription and the broadcaster removes it — otherwise the early return below
        // would leak the subscriber for an already-finished group.
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var reader = broadcaster.SubscribeToGroup(id, streamCts.Token);
            if (group.Status is TestRunStatus.Completed or TestRunStatus.Failed or TestRunStatus.Cancelled)
            {
                var completeEvt = GroupRunCompleteEvent.Create(group);
                var completeData = JsonSerializer.Serialize(completeEvt, ApiJsonOptions.Sse);
                await Response.WriteAsync($"event: group-run-complete\ndata: {completeData}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                return;
            }

            await foreach (var evt in SseWriter.ReadWithHeartbeatAsync(reader, streamCts.Token))
            {
                if (evt is null)
                {
                    await SseWriter.WriteHeartbeatAsync(Response, cancellationToken);
                    continue;
                }

                await WriteEventAsync(evt, cancellationToken);
            }
        }
        finally
        {
            await streamCts.CancelAsync();
        }
    }

    [HttpPost("{id:guid}/optimize")]
    public async Task<IActionResult> Optimize(Guid id, CancellationToken cancellationToken)
    {
        var group = await groupRepository.FindAsync(id, cancellationToken);
        if (group is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        if (group.Status is not TestRunStatus.Completed)
            return BadRequest("Only completed test run groups can be optimized.");
        await optimizerService.EnqueueAsync(group, cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunGroupOptimizeRequested, nameof(ITestRunGroup), group.Id, group.Suite.Name,
            projectId: group.Suite.Agent.Project.Id);
        return Accepted();
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<TestRunGroupDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var group = await groupRepository.FindAsync(id, cancellationToken);
        if (group is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        group = await runner.CancelAsync(group, cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunGroupCancelled, nameof(ITestRunGroup), group.Id, group.Suite.Name,
            projectId: group.Suite.Agent.Project.Id);
        return AcceptedAtAction(nameof(Get), new { id = group.Id }, await ToDtoAsync(group, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var group = await groupRepository.FindAsync(id, cancellationToken);
        if (group is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        var result = await this.DeleteOrConflictAsync(
            () => groupRepository.RemoveAsync(id, cancellationToken),
            "This run group still has runs referenced by an optimization proposal. Remove the proposal first.");
        if (result is NoContentResult)
        {
            audit.LogAudit(
                AuditAction.TestRunGroupDeleted, nameof(ITestRunGroup), id, group.Suite.Name,
                projectId: group.Suite.Agent.Project.Id);
        }

        return result;
    }

    private async Task<TestRunGroupDto> ToDtoAsync(ITestRunGroup group, CancellationToken cancellationToken)
    {
        var runs = await runRepository.GetByGroupAsync(group.Id, cancellationToken);
        return new TestRunGroupDto(
            Id: group.Id,
            SuiteId: group.Suite.Id,
            SuiteName: group.Suite.Name,
            AgentId: group.Suite.Agent.Id,
            AgentName: group.Suite.Agent.Name,
            Status: group.Status,
            IsSystemRun: group.IsSystemRun,
            SampleCount: group.SampleCount,
            CompletedAt: group.CompletedAt,
            Runs: runs.Select(runMapper.ToDto).ToArray(),
            CreatedAt: group.CreatedAt,
            UpdatedAt: group.UpdatedAt);
    }

    private async Task WriteEventAsync(TestRunEvent evt, CancellationToken cancellationToken)
    {
        string eventName = evt switch
        {
            GroupRunCompleteEvent => "group-run-complete",
            TestCaseStartedEvent => "test-case-started",
            InferenceDoneEvent => "inference-done",
            EvaluationArrivedEvent => "evaluation-arrived",
            TestResultArrivedEvent => "test-result-arrived",
            RunCompleteEvent => "run-complete",
            _ => "unknown",
        };
        var data = SseEventSerializer.Serialize(evt, evt.GetType());
        await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
