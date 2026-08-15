using Proxytrace.Domain.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Evaluators;
using Proxytrace.Api.Dto.Statistics;
using Proxytrace.Api.Evaluators;
using Proxytrace.Application.Evaluator;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for evaluators operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/evaluators")]
public class EvaluatorsController : ControllerBase
{
    private readonly IEvaluatorRepository evaluatorRepository;
    private readonly IProjectRepository projectRepository;
    private readonly IAgenticEvaluatorPresets agenticPresets;
    private readonly ITestResultRepository testResults;
    private readonly ITestRunRepository testRuns;
    private readonly ITestSuiteRepository testSuites;
    private readonly IEvaluatorStatsReader evaluatorStats;
    private readonly EvaluatorBuilder evaluatorBuilder;
    private readonly EvaluatorDtoMapper evaluatorMapper;
    private readonly ITransaction transaction;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="EvaluatorsController"/> class.
    /// </summary>
    public EvaluatorsController(
        IEvaluatorRepository evaluatorRepository,
        IProjectRepository projectRepository,
        IAgenticEvaluatorPresets agenticPresets,
        ITestResultRepository testResults,
        ITestRunRepository testRuns,
        ITestSuiteRepository testSuites,
        IEvaluatorStatsReader evaluatorStats,
        EvaluatorBuilder evaluatorBuilder,
        EvaluatorDtoMapper evaluatorMapper,
        ITransaction transaction,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.audit = audit;
        this.accessGuard = accessGuard;
        this.evaluatorRepository = evaluatorRepository;
        this.projectRepository = projectRepository;
        this.agenticPresets = agenticPresets;
        this.testResults = testResults;
        this.testRuns = testRuns;
        this.testSuites = testSuites;
        this.evaluatorStats = evaluatorStats;
        this.evaluatorBuilder = evaluatorBuilder;
        this.evaluatorMapper = evaluatorMapper;
        this.transaction = transaction;
    }

    /// <summary>
    /// The evaluators the caller may list under the optional project filter — the whole instance
    /// for an unfiltered admin, one project's when the scope names one, and the union of the
    /// caller's projects otherwise.
    /// </summary>
    private async Task<IReadOnlyList<IEvaluator>> ListScopedAsync(
        IReadOnlyCollection<Guid>? scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsEmpty())
            return [];
        if (scope.SingleProject() is { } projectId)
            return await evaluatorRepository.GetByProjectAsync(projectId, cancellationToken);
        var all = await evaluatorRepository.GetAllAsync(cancellationToken);
        return scope is null ? all : all.Where(e => scope.Contains(e.Project.Id)).ToArray();
    }

    /// <summary>
    /// The suites behind the overview's evaluator→suite references, scoped the same way as
    /// <see cref="ListScopedAsync"/>.
    /// </summary>
    private async Task<IReadOnlyList<ITestSuite>> ListScopedSuitesAsync(
        IReadOnlyCollection<Guid>? scope,
        CancellationToken cancellationToken)
    {
        if (scope.IsEmpty())
            return [];
        if (scope.SingleProject() is { } projectId)
            return await testSuites.GetByProjectAsync(projectId, cancellationToken);
        var all = await testSuites.GetAllAsync(cancellationToken);
        return scope is null ? all : all.Where(s => scope.Contains(s.Agent.Project.Id)).ToArray();
    }

    /// <summary>
    /// Gets the agentic presets.
    /// </summary>
    [HttpGet("agentic-presets")]
    public IReadOnlyList<AgenticEvaluatorPresetDto> GetAgenticPresets()
        => agenticPresets.GetAll()
            .Select(p => new AgenticEvaluatorPresetDto(p.Key, p.Name, p.SystemPrompt))
            .ToArray();

    /// <summary>
    /// Gets the all.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<EvaluatorDetailDto>> GetAll(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        var all = await ListScopedAsync(scope, cancellationToken);
        return all.Select(evaluatorMapper.ToDto).ToArray();
    }

    /// <summary>
    /// Gets the summaries.
    /// </summary>
    [HttpGet("summaries")]
    public async Task<IReadOnlyList<EvaluatorListItemDto>> GetSummaries(
        [FromQuery] Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        var all = await ListScopedAsync(scope, cancellationToken);
        return all.Select(e => new EvaluatorListItemDto(e.Id, e.Kind, e.Name)).ToArray();
    }

    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EvaluatorDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var evaluator = await evaluatorRepository.FindAsync(id, cancellationToken);
        if (evaluator is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(evaluator.Project.Id, cancellationToken))
            return NotFound();
        return evaluatorMapper.ToDto(evaluator);
    }

    /// <summary>
    /// Gets the overview.
    /// </summary>
    [HttpGet("overview")]
    public async Task<EvaluatorsOverviewDto> GetOverview(
        [FromQuery] Guid? projectId = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] StatisticsBucket bucket = StatisticsBucket.Daily,
        CancellationToken cancellationToken = default)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope.IsEmpty())
            return new EvaluatorsOverviewDto([], [], []);

        // The sparkline query is project-scoped, so it runs for every restricted scope — one
        // project's when the request named one (or the caller is a REST API key, confined to its
        // own), and the caller's whole membership otherwise (#483). An unrestricted admin scope
        // (null) still gets no sparklines: there is no project set to key them on, and the
        // instance-wide series is not what that view shows.
        Task<IReadOnlyList<IEvaluator>> evaluatorsTask = ListScopedAsync(scope, cancellationToken);
        Task<IReadOnlyList<ITestSuite>> suitesTask = ListScopedSuitesAsync(scope, cancellationToken);
        Task<IReadOnlyList<EvaluatorSparklineStat>> sparklinesTask = scope is { Count: > 0 } sparklineProjects && from.HasValue && to.HasValue
            ? evaluatorStats.GetSparklinesAsync(sparklineProjects, from.Value, to.Value, bucket, cancellationToken)
            : Task.FromResult<IReadOnlyList<EvaluatorSparklineStat>>([]);

        await Task.WhenAll(evaluatorsTask, suitesTask, sparklinesTask);

        return new EvaluatorsOverviewDto(
            Evaluators: evaluatorsTask.Result.Select(evaluatorMapper.ToDto).ToArray(),
            Suites: suitesTask.Result.Select(s => new EvaluatorSuiteRefDto(
                s.Id,
                s.Name,
                s.Agent.Id,
                s.Agent.Name,
                s.Evaluators.Select(e => e.Id).ToArray())).ToArray(),
            Sparklines: sparklinesTask.Result.Select(EvaluatorStatsDtoMapper.ToDto).ToArray());
    }

    /// <summary>
    /// Gets the detail view.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<EvaluatorDetailViewDto>> GetDetailView(
        Guid id,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] StatisticsBucket bucket = StatisticsBucket.Daily,
        [FromQuery] int recentCount = 8,
        CancellationToken cancellationToken = default)
    {
        if (from is null || to is null)
            return BadRequest("Query parameters 'from' and 'to' are required.");
        var evaluator = await evaluatorRepository.FindAsync(id, cancellationToken);
        if (evaluator is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(evaluator.Project.Id, cancellationToken))
            return NotFound();

        var capped = Math.Clamp(recentCount, 1, 50);
        Task<EvaluatorOverviewStat> overviewTask = evaluatorStats.GetOverviewAsync(id, from.Value, to.Value, bucket, cancellationToken);
        Task<IReadOnlyList<ITestResult>> recentTask = testResults.GetRecentByEvaluatorAsync(id, capped, cancellationToken: cancellationToken);

        await Task.WhenAll(overviewTask, recentTask);

        var recent = recentTask.Result;
        var runIds = await testRuns.GetRunIdsByResultIdsAsync(recent.Select(r => r.Id).ToArray(), cancellationToken);

        return new EvaluatorDetailViewDto(
            Overview: EvaluatorStatsDtoMapper.ToDto(overviewTask.Result),
            RecentEvaluations: recent
                .Select(r => evaluatorMapper.ToRecentDto(r, id, runIds.TryGetValue(r.Id, out var runId) ? runId : null))
                .ToArray());
    }

    /// <summary>
    /// Creates.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EvaluatorDetailDto>> Create(
        [FromBody] CreateEvaluatorRequest request,
        CancellationToken cancellationToken)
    {
        // Emit the audit only AFTER the transaction commits — LogAudit is a fire-and-forget write to a
        // decoupled channel, so emitting inside the lambda would leave a phantom audit row if the commit
        // then fails. Capture the payload here and log once InvokeAsync has returned (committed).
        (Guid Id, string Name, Guid ProjectId)? created = null;
        var result = await transaction.InvokeAsync<ActionResult<EvaluatorDetailDto>>(async () =>
        {
            var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
            if (project is null)
                return BadRequest($"Project {request.ProjectId} not found.");
            if (!await accessGuard.CanAccessProjectAsync(project.Id, cancellationToken))
                return NotFound();

            var evaluator = await evaluatorBuilder.BuildAsync(request, project, cancellationToken);
            var saved = await evaluatorRepository.AddAsync(evaluator, cancellationToken);
            created = (saved.Id, saved.Name, project.Id);
            return CreatedAtAction(nameof(Get), new { id = saved.Id }, evaluatorMapper.ToDto(saved));
        });

        if (created is { } c)
            audit.LogAudit(AuditAction.EvaluatorCreated, nameof(IEvaluator), c.Id, c.Name, projectId: c.ProjectId);
        return result;
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EvaluatorDetailDto>> Update(
        Guid id,
        [FromBody] UpdateEvaluatorRequest request,
        CancellationToken cancellationToken)
    {
        (Guid Id, string Name, Guid ProjectId)? updated = null;
        var result = await transaction.InvokeAsync<ActionResult<EvaluatorDetailDto>>(async () =>
        {
            var existing = await evaluatorRepository.FindAsync(id, cancellationToken);
            if (existing is null)
                return NotFound();
            if (!await accessGuard.CanAccessProjectAsync(existing.Project.Id, cancellationToken))
                return NotFound();

            var evaluator = await evaluatorBuilder.BuildAsync(request, existing, cancellationToken);
            var saved = await evaluatorRepository.UpdateAsync(evaluator, cancellationToken);
            updated = (saved.Id, saved.Name, saved.Project.Id);
            return evaluatorMapper.ToDto(saved);
        });

        if (updated is { } u)
            audit.LogAudit(AuditAction.EvaluatorUpdated, nameof(IEvaluator), u.Id, u.Name, projectId: u.ProjectId);
        return result;
    }

    /// <summary>
    /// Deletes.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var evaluator = await evaluatorRepository.FindAsync(id, cancellationToken);
        if (evaluator is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(evaluator.Project.Id, cancellationToken))
            return NotFound();

        // Soft-delete: archiving hides the evaluator and detaches it from suites, but keeps the row
        // so historical test results still resolve it. See ArchivableRepository.
        var archived = await evaluatorRepository.ArchiveAsync(id, cancellationToken);
        if (!archived)
            return NotFound();

        var projectId = await evaluatorRepository.GetProjectIdAsync(id, cancellationToken);
        audit.LogAudit(AuditAction.EvaluatorDeleted, nameof(IEvaluator), id, evaluator.Name, projectId: projectId);
        return NoContent();
    }

    /// <summary>
    /// Recent evaluations.
    /// </summary>
    [HttpGet("{id:guid}/recent-evaluations")]
    public async Task<ActionResult<IReadOnlyList<RecentEvaluationItemDto>>> RecentEvaluations(
        Guid id,
        [FromQuery] int count = 8,
        [FromQuery] EvaluationScore? score = null,
        CancellationToken cancellationToken = default)
    {
        var evaluator = await evaluatorRepository.FindAsync(id, cancellationToken);
        if (evaluator is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(evaluator.Project.Id, cancellationToken))
            return NotFound();

        var capped = Math.Clamp(count, 1, 50);
        var recent = await testResults.GetRecentByEvaluatorAsync(id, capped, score, cancellationToken);
        var runIds = await testRuns.GetRunIdsByResultIdsAsync(recent.Select(r => r.Id).ToArray(), cancellationToken);

        return recent
            .Select(r => evaluatorMapper.ToRecentDto(r, id, runIds.TryGetValue(r.Id, out var runId) ? runId : null))
            .ToArray();
    }
}
