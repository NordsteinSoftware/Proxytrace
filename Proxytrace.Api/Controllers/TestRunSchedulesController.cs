using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for test run schedules operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/test-run-schedules")]
public class TestRunSchedulesController : ControllerBase
{
    private readonly ITestRunScheduleRepository scheduleRepository;
    private readonly ITestRunGroupRepository groupRepository;
    private readonly ITestRunRepository runRepository;
    private readonly ITestSuiteRepository suiteRepository;
    private readonly IAgentRepository agentRepository;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly ITestRunSchedule.CreateNew createSchedule;
    private readonly ITestRunnerService runner;
    private readonly TestRunDtoMapper runMapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunSchedulesController"/> class.
    /// </summary>
    public TestRunSchedulesController(
        ITestRunScheduleRepository scheduleRepository,
        ITestRunGroupRepository groupRepository,
        ITestRunRepository runRepository,
        ITestSuiteRepository suiteRepository,
        IAgentRepository agentRepository,
        IRepository<IModelEndpoint> endpoints,
        ITestRunSchedule.CreateNew createSchedule,
        ITestRunnerService runner,
        TestRunDtoMapper runMapper,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.scheduleRepository = scheduleRepository;
        this.groupRepository = groupRepository;
        this.runRepository = runRepository;
        this.suiteRepository = suiteRepository;
        this.agentRepository = agentRepository;
        this.endpoints = endpoints;
        this.createSchedule = createSchedule;
        this.runner = runner;
        this.runMapper = runMapper;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    // Resolve the effective owning project of a list query and verify access. Admins
    // (accessible == null) pass for any scope. Non-admins must scope to a project they belong to —
    // directly via projectId or via the agent's project — otherwise the query returns nothing rather
    // than leaking other tenants' rows.
    /// <summary>
    /// The schedules the caller may list under the optional agent/project filters. An agent filter
    /// authorizes against that agent's project and then queries by agent; otherwise the request is
    /// scoped to the projects the caller may read.
    /// </summary>
    private async Task<IReadOnlyList<ITestRunSchedule>> ListScopedAsync(
        Guid? agentId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope.IsEmpty())
            return [];

        if (agentId is { } aid)
        {
            var agent = await agentRepository.FindAsync(aid, cancellationToken);
            if (agent is null || !scope.Admits(agent.Project.Id))
                return [];
            return await scheduleRepository.GetByAgentAsync(aid, cancellationToken);
        }

        if (scope.SingleProject() is { } singleProject)
            return await scheduleRepository.GetByProjectAsync(singleProject, cancellationToken);

        var all = await scheduleRepository.GetAllAsync(cancellationToken);
        return scope is null ? all : all.Where(s => scope.Contains(s.Suite.Agent.Project.Id)).ToArray();
    }

    /// <summary>
    /// Lists schedules. Ungated so existing schedules stay visible after a license downgrade —
    /// only creation/management is feature-gated.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<TestRunScheduleDto>> GetAll(
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ITestRunSchedule> schedules = await ListScopedAsync(agentId, projectId, cancellationToken);

        return await Task.WhenAll(schedules.Select(s => ToDtoAsync(s, cancellationToken)));
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestRunScheduleDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepository.FindAsync(id, cancellationToken);
        if (schedule is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(schedule.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        return await ToDtoAsync(schedule, cancellationToken);
    }

    /// <summary>
    /// Creates.
    /// </summary>
    [HttpPost]
    [RequiresFeature(LicenseFeature.ScheduledTestRuns)]
    public async Task<ActionResult<TestRunScheduleDto>> Create(
        [FromBody] CreateTestRunScheduleRequest request,
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
            return BadRequest($"A schedule can target at most {ITestRunGroup.MaxModelEndpoints} model endpoints.");

        if (request.IntervalMinutes < 1)
            return BadRequest("Interval must be at least one minute.");

        var endpointList = await Task.WhenAll(
            request.ModelEndpointIds.Select(id => endpoints.GetAsync(id, cancellationToken)));

        var schedule = createSchedule(
            request.Name, suite, endpointList, TimeSpan.FromMinutes(request.IntervalMinutes), request.Enabled,
            request.AnchorAt ?? DateTimeOffset.UtcNow);
        schedule = await scheduleRepository.AddAsync(schedule, cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunScheduleCreated, nameof(ITestRunSchedule), schedule.Id, schedule.Name,
            projectId: schedule.Suite.Agent.Project.Id);

        return CreatedAtAction(nameof(Get), new { id = schedule.Id }, await ToDtoAsync(schedule, cancellationToken));
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequiresFeature(LicenseFeature.ScheduledTestRuns)]
    public async Task<ActionResult<TestRunScheduleDto>> Update(
        Guid id,
        [FromBody] UpdateTestRunScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepository.FindAsync(id, cancellationToken);
        if (schedule is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(schedule.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        if (request.ModelEndpointIds.Count == 0)
            return BadRequest("At least one endpoint must be specified.");

        if (request.ModelEndpointIds.Count > ITestRunGroup.MaxModelEndpoints)
            return BadRequest($"A schedule can target at most {ITestRunGroup.MaxModelEndpoints} model endpoints.");

        if (request.IntervalMinutes < 1)
            return BadRequest("Interval must be at least one minute.");

        var endpointList = await Task.WhenAll(
            request.ModelEndpointIds.Select(eid => endpoints.GetAsync(eid, cancellationToken)));

        schedule = await schedule.Update(
            request.Name, endpointList, TimeSpan.FromMinutes(request.IntervalMinutes), request.Enabled,
            request.AnchorAt ?? schedule.AnchorAt, DateTimeOffset.UtcNow, cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunScheduleUpdated, nameof(ITestRunSchedule), schedule.Id, schedule.Name,
            projectId: schedule.Suite.Agent.Project.Id);

        return await ToDtoAsync(schedule, cancellationToken);
    }

    /// <summary>
    /// Deletes.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequiresFeature(LicenseFeature.ScheduledTestRuns)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepository.FindAsync(id, cancellationToken);
        if (schedule is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(schedule.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        await scheduleRepository.RemoveAsync(id, cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunScheduleDeleted, nameof(ITestRunSchedule), id, schedule.Name,
            projectId: schedule.Suite.Agent.Project.Id);
        return NoContent();
    }

    /// <summary>
    /// Runs the now.
    /// </summary>
    [HttpPost("{id:guid}/run-now")]
    [RequiresFeature(LicenseFeature.ScheduledTestRuns)]
    public async Task<ActionResult<TestRunScheduleDto>> RunNow(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await scheduleRepository.FindAsync(id, cancellationToken);
        if (schedule is null)
            return NotFound();

        if (!await accessGuard.CanAccessProjectAsync(schedule.Suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        await runner.RunInBackgroundAsync(
            schedule.Suite, schedule.Endpoints.ToArray(), schedule.Id, cancellationToken: cancellationToken);
        audit.LogAudit(
            AuditAction.TestRunScheduleRunNow, nameof(ITestRunSchedule), schedule.Id, schedule.Name,
            projectId: schedule.Suite.Agent.Project.Id);

        return await ToDtoAsync(schedule, cancellationToken);
    }

    private async Task<TestRunScheduleDto> ToDtoAsync(ITestRunSchedule schedule, CancellationToken cancellationToken)
    {
        var recentGroups = await groupRepository.GetByScheduleAsync(schedule.Id, 5, cancellationToken);
        var recentRuns = await Task.WhenAll(
            recentGroups.Select(g => runMapper.ToListItemDtoAsync(g, runRepository, cancellationToken)));

        return new TestRunScheduleDto(
            Id: schedule.Id,
            Name: schedule.Name,
            SuiteId: schedule.Suite.Id,
            SuiteName: schedule.Suite.Name,
            AgentId: schedule.Suite.Agent.Id,
            AgentName: schedule.Suite.Agent.Name,
            Endpoints: schedule.Endpoints.Select(e => new ScheduleEndpointDto(e.Id, e.Model.Name)).ToArray(),
            IntervalMinutes: (int)schedule.Interval.TotalMinutes,
            IsEnabled: schedule.IsEnabled,
            AnchorAt: schedule.AnchorAt,
            NextRunAt: schedule.NextRunAt,
            LastRunAt: schedule.LastRunAt,
            RecentRuns: recentRuns,
            CreatedAt: schedule.CreatedAt,
            UpdatedAt: schedule.UpdatedAt);
    }
}
