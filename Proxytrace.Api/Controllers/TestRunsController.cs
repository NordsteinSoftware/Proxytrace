using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Api.Json;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/test-runs")]
public class TestRunsController : ControllerBase
{
    private readonly ITestRunRepository repository;
    private readonly IAgentRepository agentRepository;
    private readonly ITestResultBroadcaster broadcaster;
    private readonly TestRunDtoMapper mapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    public TestRunsController(
        ITestRunRepository repository,
        IAgentRepository agentRepository,
        ITestResultBroadcaster broadcaster,
        TestRunDtoMapper mapper,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.repository = repository;
        this.agentRepository = agentRepository;
        this.broadcaster = broadcaster;
        this.mapper = mapper;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    [HttpGet]
    public async Task<PagedResult<TestRunDto>> GetAll(
        [FromQuery] Guid? agentId = null,
        [FromQuery] bool includeSystem = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // includeSystem false (default) hides ephemeral A/B validation runs, matching the runs page
        // and /api/test-run-groups; pass true only to surface those internal system runs.
        if (agentId.HasValue)
        {
            var agent = await agentRepository.FindAsync(agentId.Value, cancellationToken);
            if (agent is null || !await accessGuard.CanAccessProjectAsync(agent.Project.Id, cancellationToken))
                return new PagedResult<TestRunDto>([], 0, page, pageSize);
            var pagedByAgent = await repository.GetByAgentPagedAsync(agentId.Value, page, pageSize, includeSystem, cancellationToken);
            return pagedByAgent.Map(mapper.ToDto);
        }

        // No agent filter: the caller's own reach is the scope — every project for an admin (null),
        // their memberships otherwise, so an unfiltered request answers with that caller's runs
        // instead of an empty page (#482).
        var scope = await accessGuard.ResolveListScopeAsync(requestedProjectId: null, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<TestRunDto>([], 0, page, pageSize);

        var paged = scope is null
            ? await repository.GetAllPagedAsync(page, pageSize, includeSystem, cancellationToken)
            : await repository.GetByProjectsPagedAsync(scope, page, pageSize, includeSystem, cancellationToken);
        return paged.Map(mapper.ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestRunDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var run = await repository.FindAsync(id, cancellationToken);
        if (run is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(run.Group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        return mapper.ToDto(run);
    }

    [HttpGet("{id:guid}/cases/{caseId:guid}/fixture")]
    public async Task<ActionResult<TestCaseFixtureDto>> GetCaseFixture(
        Guid id, Guid caseId, CancellationToken cancellationToken)
    {
        var run = await repository.FindAsync(id, cancellationToken);
        if (run is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(run.Group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        var result = run.TestResults.FirstOrDefault(r => r.TestCase.Id == caseId);
        if (result is null)
            return NotFound();
        return mapper.ToFixtureDto(run, result);
    }

    [HttpGet("{id:guid}/cases/{caseId:guid}/request")]
    public async Task<ActionResult<ModelRequestPreviewDto>> GetCaseRequest(
        Guid id, Guid caseId, CancellationToken cancellationToken)
    {
        var run = await repository.FindAsync(id, cancellationToken);
        if (run is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(run.Group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        var testCase = run.Group.Suite.TestCases.FirstOrDefault(tc => tc.Id == caseId);
        if (testCase is null)
            return NotFound();

        var client = run.Group.Suite.Agent.CreateClient(customEndpoint: run.Endpoint, skipIngestion: true);
        var preview = client.BuildRequestPreview(testCase.Input);
        return mapper.ToRequestDto(preview);
    }

    [HttpGet("{id:guid}/stream")]
    public async Task Stream(Guid id, CancellationToken cancellationToken)
    {
        var run = await repository.FindAsync(id, cancellationToken);
        if (run is null || !await accessGuard.CanAccessProjectAsync(run.Group.Suite.Agent.Project.Id, cancellationToken))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // Subscribe through a linked token so every exit path (the already-terminal early return,
        // normal completion, client disconnect, exceptions) cancels it and the broadcaster removes
        // the subscription. Subscribing before the terminal check keeps it race-free: a run that
        // completes in the gap still delivers run-complete through the channel.
        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var reader = broadcaster.Subscribe(id, streamCts.Token);
            if (run.Status is TestRunStatus.Completed or TestRunStatus.Failed or TestRunStatus.Cancelled)
            {
                var completeEvt = RunCompleteEvent.Create(run);
                var completeData = JsonSerializer.Serialize(completeEvt, completeEvt.GetType(), ApiJsonOptions.Sse);
                await Response.WriteAsync($"event: run-complete\ndata: {completeData}\n\n", cancellationToken);
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
            // Triggers the broadcaster's unsubscribe even when we return early (terminal run).
            await streamCts.CancelAsync();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var run = await repository.FindAsync(id, cancellationToken);
        if (run is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(run.Group.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        var result = await this.DeleteOrConflictAsync(
            () => repository.RemoveAsync(id, cancellationToken),
            "This test run is still referenced by an optimization proposal. Remove the proposal before deleting the run.");
        if (result is NoContentResult)
        {
            audit.LogAudit(
                AuditAction.TestRunDeleted, nameof(ITestRun), id, run.Group.Suite.Name,
                projectId: run.Group.Suite.Agent.Project.Id);
        }

        return result;
    }

    private async Task WriteEventAsync(TestRunEvent evt, CancellationToken cancellationToken)
    {
        var eventName = evt switch
        {
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
