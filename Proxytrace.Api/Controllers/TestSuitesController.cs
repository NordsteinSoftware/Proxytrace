using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for test suites operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/test-suites")]
public class TestSuitesController : ControllerBase
{
    private readonly ITestSuiteRepository suiteRepository;
    private readonly IAgentRepository agentRepository;
    private readonly IAgentCallRepository agentCallRepository;
    private readonly ITestCaseRepository testCaseRepository;
    private readonly IEvaluatorRepository evaluatorRepository;
    private readonly ITestCase.CreateNew createTestCase;
    private readonly ITestCase.CreateNewFromCall createTestCaseFromCall;
    private readonly ITestCase.CreateCorrection createTestCaseCorrection;
    private readonly IExactMatchEvaluator.CreateNew createEvaluator;
    private readonly ITestSuite.CreateNew createSuite;
    private readonly ITestSuite.CreateExisting createSuiteExisting;
    private readonly TestSuiteDtoMapper mapper;
    private readonly IStatsReader<TestRunStats, TestRunStats.Filter> runStats;
    private readonly ILicenseService license;
    private readonly IProjectAccessGuard accessGuard;
    private readonly IAsyncLock asyncLock;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Lock key serializing the licensed-suite-count check against the create that follows it.
    /// </summary>
    /// <remarks>
    /// The limit is installation-wide, so the key is a constant rather than per-project. Mirrors
    /// <c>TheoryValidationService.SubmitAsync</c>, which serializes its own check-then-act quota the
    /// same way. Like that one this is per-process: it closes the ordinary double-submit race, not a
    /// race between two replicas — enforcing across replicas needs a database constraint, which a
    /// count-based limit cannot express.
    /// </remarks>
    private const string SuiteQuotaLockKey = "license-quota:test-suites";

    /// <summary>
    /// Initializes a new instance of the <see cref="TestSuitesController"/> class.
    /// </summary>
    public TestSuitesController(
        ITestSuiteRepository suiteRepository,
        IAgentRepository agentRepository,
        IAgentCallRepository agentCallRepository,
        ITestCaseRepository testCaseRepository,
        IEvaluatorRepository evaluatorRepository,
        ITestCase.CreateNew createTestCase,
        ITestCase.CreateNewFromCall createTestCaseFromCall,
        ITestCase.CreateCorrection createTestCaseCorrection,
        IExactMatchEvaluator.CreateNew createEvaluator,
        ITestSuite.CreateNew createSuite,
        ITestSuite.CreateExisting createSuiteExisting,
        TestSuiteDtoMapper mapper,
        IStatsReader<TestRunStats, TestRunStats.Filter> runStats,
        ILicenseService license,
        IProjectAccessGuard accessGuard,
        IAsyncLock asyncLock,
        ILogger<Audit> audit)
    {
        this.audit = audit;
        this.accessGuard = accessGuard;
        this.asyncLock = asyncLock;
        this.suiteRepository = suiteRepository;
        this.agentRepository = agentRepository;
        this.agentCallRepository = agentCallRepository;
        this.testCaseRepository = testCaseRepository;
        this.evaluatorRepository = evaluatorRepository;
        this.createTestCase = createTestCase;
        this.createTestCaseFromCall = createTestCaseFromCall;
        this.createTestCaseCorrection = createTestCaseCorrection;
        this.createEvaluator = createEvaluator;
        this.createSuite = createSuite;
        this.createSuiteExisting = createSuiteExisting;
        this.mapper = mapper;
        this.runStats = runStats;
        this.license = license;
    }

    // Caller-supplied evaluator and test-case ids are not implicitly the caller's. Without these
    // guards a crafted id lets a member of one project attach — and, because the response echoes the
    // saved suite, read back — another project's evaluator or test-case conversation and expected
    // output, and spend that project's provider credential when the suite runs its agentic judge.
    // Same shape as EvaluatorTestBenchController's guards (#265): resolve the owning project, deny
    // behind a 404 rather than a 403 so an id cannot be used as an existence oracle.
    private async Task<bool> CanAccessEvaluatorsAsync(
        IReadOnlyCollection<Guid> evaluatorIds,
        CancellationToken cancellationToken)
    {
        foreach (var evaluatorId in evaluatorIds)
        {
            var projectId = await evaluatorRepository.GetProjectIdAsync(evaluatorId, cancellationToken);
            if (projectId is null || !await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken))
                return false;
        }

        return true;
    }

    // A test case carries no project of its own — it is reachable only through the suite that
    // references it — so an orphaned case (its suite was deleted) has no resolvable owner and stays
    // accessible, matching EvaluatorTestBenchController.CanAccessTestCaseAsync.
    private async Task<bool> CanAccessTestCasesAsync(
        IReadOnlyCollection<Guid> testCaseIds,
        CancellationToken cancellationToken)
    {
        foreach (var testCaseId in testCaseIds)
        {
            var projectId = await suiteRepository.GetProjectIdByTestCaseAsync(testCaseId, cancellationToken);
            if (projectId is not null && !await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Gets the all.
    /// </summary>
    [HttpGet]
    public async Task<PagedResult<TestSuiteListItemDto>> GetAll(
        [FromQuery] Guid? agentId = null,
        [FromQuery] Guid? projectId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var scopeProjectId = projectId;
        if (agentId.HasValue)
        {
            var scopeAgent = await agentRepository.FindAsync(agentId.Value, cancellationToken);
            if (scopeAgent is null)
                return new PagedResult<TestSuiteListItemDto>([], 0, page, pageSize);
            scopeProjectId = scopeAgent.Project.Id;
        }

        var scope = await accessGuard.ResolveListScopeAsync(scopeProjectId, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<TestSuiteListItemDto>([], 0, page, pageSize);

        PagedResult<ITestSuite> paged;
        if (agentId.HasValue)
            paged = await suiteRepository.GetByAgentPagedAsync(agentId.Value, page, pageSize, cancellationToken);
        else if (scope is null)
            paged = await suiteRepository.GetPagedAsync(page, pageSize, cancellationToken);
        else
            paged = await suiteRepository.GetByProjectsPagedAsync(scope, page, pageSize, cancellationToken);

        var statsBySuite = await GetRunStatsBySuiteAsync(
            paged.Items.Select(s => s.Id).ToArray(), cancellationToken);

        return paged.Map(s => mapper.ToListItemDto(
            s,
            statsBySuite.TryGetValue(s.Id, out var rows) ? rows : []));
    }

    /// <summary>
    /// Loads finalized run statistics for the given suites, grouped by suite id.
    /// Returns an empty map when no suite ids are supplied.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<TestRunStats>>> GetRunStatsBySuiteAsync(
        IReadOnlyCollection<Guid> suiteIds,
        CancellationToken cancellationToken)
    {
        if (suiteIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<TestRunStats>>();

        // Scope the projection to the wanted suites in SQL (WHERE SuiteId IN (...)) rather than
        // materializing the whole TestRunStats table and filtering in memory — the latter is
        // O(all-rows) on every suites list and single-suite GET as run history grows (#253).
        var rows = await runStats.QueryAsync(
            new TestRunStats.Filter(SuiteIds: suiteIds), cancellationToken);
        return rows
            .GroupBy(r => r.SuiteId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TestRunStats>)g.ToArray());
    }


    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TestSuiteDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var suite = await suiteRepository.FindAsync(id, cancellationToken);
        if (suite is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(suite.Agent.Project.Id, cancellationToken))
            return NotFound();
        var statsBySuite = await GetRunStatsBySuiteAsync([id], cancellationToken);
        return mapper.ToDto(
            suite,
            statsBySuite.TryGetValue(id, out var rows) ? rows : []);
    }

    /// <summary>
    /// Bucket (time-window) run statistics for a suite: run count, pass rate, average run duration,
    /// and total cost over the optional [from, to] window. Reuses the per-run stats projection — no
    /// per-test-case aggregation. Omitting both bounds yields all-time stats.
    /// </summary>
    [HttpGet("{id:guid}/run-stats")]
    public async Task<ActionResult<SuiteRunStatsDto>> GetRunStats(
        Guid id,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var suite = await suiteRepository.FindAsync(id, cancellationToken);
        if (suite is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        var rows = await runStats.QueryAsync(
            new TestRunStats.Filter(SuiteId: id, From: from, To: to), cancellationToken);
        return mapper.ToRunStatsDto(rows);
    }

    /// <summary>
    /// Creates.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TestSuiteDto>> Create(
        [FromBody] CreateTestSuiteRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");

        var agent = await agentRepository.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return BadRequest($"Agent {request.AgentId} not found.");
        if (!await accessGuard.CanAccessProjectAsync(agent.Project.Id, cancellationToken))
            return NotFound();

        // Held to the end of the method, so the count and the create that acts on it cannot be
        // interleaved by a concurrent request — two simultaneous creates both used to observe
        // "0 suites" and both proceed, taking a Free-tier install to two.
        using IDisposable quotaLock = await asyncLock.LockAsync(SuiteQuotaLockKey, cancellationToken);
        license.Ensure(LicenseLimit.MaxTestSuites, await suiteRepository.CountAsync(cancellationToken));

        IReadOnlyCollection<IEvaluator> evaluators;
        if (request.EvaluatorIds is { Count: > 0 })
        {
            var distinctEvalIds = request.EvaluatorIds.Distinct().ToArray();
            if (!await CanAccessEvaluatorsAsync(distinctEvalIds, cancellationToken))
                return NotFound();
            evaluators = await evaluatorRepository.GetManyAsync(distinctEvalIds, cancellationToken: cancellationToken);
        }
        else
        {
            var defaultEvaluator = createEvaluator(agent.Project);
            var savedDefault = await evaluatorRepository.AddAsync(defaultEvaluator, cancellationToken);
            evaluators = [savedDefault];
        }

        var testCases = new List<ITestCase>();
        foreach (var tc in request.TestCases)
        {
            var testCase = await BuildTestCase(tc.FromAgentCallId, tc.Input, tc.ExpectedOutput, cancellationToken);
            if (testCase is null)
                return BadRequest("Each test case must have either fromAgentCallId or both input and expectedOutput.");
            var saved = await testCaseRepository.AddAsync(testCase, cancellationToken);
            testCases.Add(saved);
        }

        var suite = createSuite(request.Name, agent, evaluators, testCases);
        var savedSuite = await suiteRepository.AddAsync(suite, cancellationToken);
        var projectId = await agentRepository.GetProjectIdAsync(agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestSuiteCreated, nameof(ITestSuite), savedSuite.Id, savedSuite.Name, projectId: projectId);
        return CreatedAtAction(nameof(Get), new { id = savedSuite.Id }, mapper.ToDto(savedSuite));
    }

    /// <summary>
    /// Updates.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<TestSuiteDto>> Update(
        Guid id,
        [FromBody] UpdateTestSuiteRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await suiteRepository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound();

        // Re-parenting the suite moves it — and every test case it carries — under another agent, so
        // the target agent's project needs the same access check as the suite's own.
        var agent = existing.Agent;
        if (request.AgentId.HasValue && request.AgentId.Value != existing.Agent.Id)
        {
            var requestedAgent = await agentRepository.FindAsync(request.AgentId.Value, cancellationToken);
            if (requestedAgent is null
                || !await accessGuard.CanAccessProjectAsync(requestedAgent.Project.Id, cancellationToken))
            {
                return NotFound($"Agent {request.AgentId.Value} not found.");
            }

            agent = requestedAgent;
        }

        IReadOnlyCollection<IEvaluator> evaluators = existing.Evaluators;
        if (request.EvaluatorIds is not null)
        {
            var distinctEvalIds = request.EvaluatorIds.Distinct().ToArray();
            if (!await CanAccessEvaluatorsAsync(distinctEvalIds, cancellationToken))
                return NotFound();
            evaluators = await evaluatorRepository.GetManyAsync(distinctEvalIds, cancellationToken: cancellationToken);
        }

        IReadOnlyCollection<ITestCase> testCases = existing.TestCases;
        if (request.TestCaseIds is not null)
        {
            var distinctCaseIds = request.TestCaseIds.Distinct().ToArray();
            if (!await CanAccessTestCasesAsync(distinctCaseIds, cancellationToken))
                return NotFound();
            testCases = await testCaseRepository.GetManyAsync(distinctCaseIds, cancellationToken: cancellationToken);
        }

        var updated = createSuiteExisting(existing.Name, agent, evaluators, testCases, existing);
        var saved = await suiteRepository.UpdateAsync(updated, cancellationToken);
        var projectId = await agentRepository.GetProjectIdAsync(agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestSuiteUpdated, nameof(ITestSuite), saved.Id, saved.Name, projectId: projectId);
        return mapper.ToDto(saved);
    }

    // Deleting a suite cascades to its run groups, runs, schedules, theories, and the proposals
    // produced from those runs (see the storage FK config) — so the delete always succeeds. The
    // DbUpdateExceptionMapper middleware still maps any unforeseen constraint to a friendly 409.
    /// <summary>
    /// Deletes.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var suite = await suiteRepository.FindAsync(id, cancellationToken);
        if (suite is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(suite.Agent.Project.Id, cancellationToken))
            return NotFound();

        if (!await suiteRepository.RemoveAsync(id, cancellationToken))
            return NotFound();

        var projectId = await agentRepository.GetProjectIdAsync(suite.Agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestSuiteDeleted, nameof(ITestSuite), id, suite.Name, projectId: projectId);
        return NoContent();
    }

    /// <summary>
    /// Creates a new test suite by promoting a curated selection of traced agent calls.
    /// Each selected trace becomes a test case whose expected output is the actual response
    /// recorded during that call, preserving the link back to the source trace.
    /// </summary>
    [HttpPost("from-traces")]
    public async Task<ActionResult<TestSuiteDto>> PromoteFromTraces(
        [FromBody] PromoteTracesRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Name is required.");
        if (request.AgentCallIds.Count == 0)
            return BadRequest("At least one agent call ID must be provided.");

        var agent = await agentRepository.FindAsync(request.AgentId, cancellationToken);
        if (agent is null)
            return BadRequest($"Agent {request.AgentId} not found.");
        if (!await accessGuard.CanAccessProjectAsync(agent.Project.Id, cancellationToken))
            return NotFound();

        // Held to the end of the method, so the count and the create that acts on it cannot be
        // interleaved by a concurrent request — two simultaneous creates both used to observe
        // "0 suites" and both proceed, taking a Free-tier install to two.
        using IDisposable quotaLock = await asyncLock.LockAsync(SuiteQuotaLockKey, cancellationToken);
        license.Ensure(LicenseLimit.MaxTestSuites, await suiteRepository.CountAsync(cancellationToken));

        IReadOnlyCollection<IEvaluator> evaluators;
        if (request.EvaluatorIds is { Count: > 0 })
        {
            var distinctEvalIds = request.EvaluatorIds.Distinct().ToArray();
            if (!await CanAccessEvaluatorsAsync(distinctEvalIds, cancellationToken))
                return NotFound();
            evaluators = await evaluatorRepository.GetManyAsync(distinctEvalIds, cancellationToken: cancellationToken);
        }
        else
        {
            var defaultEvaluator = createEvaluator(agent.Project);
            var savedDefault = await evaluatorRepository.AddAsync(defaultEvaluator, cancellationToken);
            evaluators = [savedDefault];
        }

        var testCases = new List<ITestCase>();
        foreach (var callId in request.AgentCallIds.Distinct())
        {
            var call = await agentCallRepository.FindAsync(callId, cancellationToken);
            // A trace is only promotable when the caller can access its owning project — otherwise a
            // crafted agentCallId would copy another tenant's trace content into the caller's suite.
            // Treat "no access" the same as "not found" so the id can't be used as an existence oracle.
            if (call is null || !await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
                return NotFound($"Agent call {callId} not found.");
            // A response-less call (the upstream errored or never completed) is a client-input
            // precondition, not a server fault — return 400 like the adjacent guards above rather
            // than letting a bare InvalidOperationException fall through to a generic 500.
            if (call.Response is null)
                return BadRequest($"Agent call {callId} does not have a response and cannot be promoted to a test case.");

            var testCase = createTestCaseFromCall(call);
            var saved = await testCaseRepository.AddAsync(testCase, cancellationToken);
            testCases.Add(saved);
        }

        var suite = createSuite(request.Name, agent, evaluators, testCases);
        var savedSuite = await suiteRepository.AddAsync(suite, cancellationToken);
        var projectId = await agentRepository.GetProjectIdAsync(agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestSuiteCreated, nameof(ITestSuite), savedSuite.Id, savedSuite.Name, projectId: projectId);
        return CreatedAtAction(nameof(Get), new { id = savedSuite.Id }, mapper.ToDto(savedSuite));
    }

    /// <summary>
    /// Adds the test case.
    /// </summary>
    [HttpPost("{id:guid}/test-cases")]
    public async Task<ActionResult<TestSuiteDto>> AddTestCase(
        Guid id,
        [FromBody] AddTestCaseRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await suiteRepository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound();

        var testCase = await BuildTestCase(request.FromAgentCallId, request.Input, request.ExpectedOutput, cancellationToken);
        if (testCase is null)
            return BadRequest("Provide either fromAgentCallId or both input and expectedOutput.");

        var saved = await testCaseRepository.AddAsync(testCase, cancellationToken);
        var updatedCases = existing.TestCases.Append(saved).ToArray();
        var updated = createSuiteExisting(existing.Name, existing.Agent, existing.Evaluators, updatedCases, existing);
        var savedSuite = await suiteRepository.UpdateAsync(updated, cancellationToken);
        var projectId = await agentRepository.GetProjectIdAsync(existing.Agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestCaseCreated, nameof(ITestCase), saved.Id, projectId: projectId);
        return mapper.ToDto(savedSuite);
    }

    /// <summary>
    /// Removes the test case.
    /// </summary>
    [HttpDelete("{id:guid}/test-cases/{caseId:guid}")]
    public async Task<ActionResult<TestSuiteDto>> RemoveTestCase(
        Guid id,
        Guid caseId,
        CancellationToken cancellationToken)
    {
        var existing = await suiteRepository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(existing.Agent.Project.Id, cancellationToken))
            return NotFound();
        if (existing.TestCases.All(tc => tc.Id != caseId))
            return mapper.ToDto(existing); // nothing to remove

        var updatedCases = existing.TestCases.Where(tc => tc.Id != caseId).ToArray();
        var updated = createSuiteExisting(existing.Name, existing.Agent, existing.Evaluators, updatedCases, existing);
        var saved = await suiteRepository.UpdateAsync(updated, cancellationToken);

        var projectId = await agentRepository.GetProjectIdAsync(existing.Agent.Id, cancellationToken);
        audit.LogAudit(AuditAction.TestCaseDeleted, nameof(ITestCase), caseId, projectId: projectId);
        return mapper.ToDto(saved);
    }

    private async Task<ITestCase?> BuildTestCase(
        Guid? fromAgentCallId,
        IReadOnlyList<TestSuiteMessageDto>? inputMessages,
        TestSuiteMessageDto? expectedOutput,
        CancellationToken cancellationToken)
    {
        if (fromAgentCallId.HasValue)
        {
            // Only build from a trace the caller may access; a foreign call id resolves to null here
            // and the callers map that to a generic 400 (no cross-tenant trace content disclosed).
            var call = await agentCallRepository.FindAsync(fromAgentCallId.Value, cancellationToken);
            if (call is null || !await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
                return null;
            // A correction ("the agent saw this input, and the right answer was X") keeps the link back
            // to the source trace, exactly like a straight promotion — otherwise the provenance the
            // caller most needs (which trace this regression test corrects) would be dropped.
            return expectedOutput is not null
                ? createTestCaseCorrection(call, mapper.BuildAssistantMessage(expectedOutput))
                : createTestCaseFromCall(call);
        }

        if (inputMessages is not null && expectedOutput is not null)
        {
            var conversation = mapper.BuildConversation(inputMessages);
            var expected = mapper.BuildAssistantMessage(expectedOutput);
            // A synthetic case (raw input + expected output) has no source trace.
            return createTestCase(conversation, expected, sourceAgentCallId: null);
        }

        return null;
    }
}
