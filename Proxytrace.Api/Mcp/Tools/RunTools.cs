using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>One evaluator's verdict on a failing test case.</summary>
internal sealed record McpEvaluationDto(string Evaluator, bool Passed, string? Reasoning, string? Score);

/// <summary>A failing case in a run: the actual response plus every evaluator's verdict.</summary>
internal sealed record McpRunFailureDto(Guid TestCaseId, string ActualResponse, string? OverallScore, IReadOnlyList<McpEvaluationDto> Evaluations);

/// <summary>Case-by-case comparison of two runs of a suite.</summary>
internal sealed record McpRunComparisonDto(
    int FixedCount,
    int RegressedCount,
    int UnchangedCount,
    IReadOnlyList<Guid> Fixed,
    IReadOnlyList<Guid> Regressed,
    double BaselinePassRate,
    double NewPassRate);

/// <summary>
/// MCP tools for listing, starting and cancelling test runs (run groups) in the current project.
/// </summary>
[McpServerToolType]
internal sealed class RunTools
{
    private readonly IMcpProjectAccessor project;
    private readonly ITestRunGroupRepository groups;
    private readonly ITestRunRepository runs;
    private readonly ITestSuiteRepository suites;
    private readonly ITestRunnerService runner;
    private readonly TestRunDtoMapper mapper;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunTools"/> class.
    /// </summary>
    public RunTools(
        IMcpProjectAccessor project,
        ITestRunGroupRepository groups,
        ITestRunRepository runs,
        ITestSuiteRepository suites,
        ITestRunnerService runner,
        TestRunDtoMapper mapper,
        ILogger<Audit> audit)
    {
        this.project = project;
        this.groups = groups;
        this.runs = runs;
        this.suites = suites;
        this.runner = runner;
        this.mapper = mapper;
        this.audit = audit;
    }

    [McpServerTool(Name = "list_test_runs")]
    [Description("List test runs (run groups) in the current project, newest first, with per-run status " +
                 "and pass/fail counts. System runs are excluded.")]
    /// <summary>
    /// Lists the test runs.
    /// </summary>
    public async Task<IReadOnlyList<TestRunGroupListItemDto>> ListTestRuns(
        [Description("Maximum number of runs to return (1-100, default 50).")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        var paged = await groups.GetByProjectPagedAsync(p.Id, 1, limit, includeSystem: false, cancellationToken);
        return await Task.WhenAll(
            paged.Items.Select(g => mapper.ToListItemDtoAsync(g, runs, cancellationToken)));
    }

    [McpServerTool(Name = "get_test_run")]
    [Description("Get a single test run (run group) by id, including each run's status and results. " +
                 "The run must belong to the current project.")]
    /// <summary>
    /// Gets the test run.
    /// </summary>
    public async Task<TestRunGroupDto> GetTestRun(
        [Description("The run group id (GUID), as returned by list_test_runs or start_test_run.")] Guid runGroupId,
        CancellationToken cancellationToken)
    {
        var group = await RequireGroupAsync(runGroupId, cancellationToken);
        return await ToDtoAsync(group, cancellationToken);
    }

    [McpServerTool(Name = "start_test_run")]
    [Description("Start a test run of a suite against its agent's current endpoint. Returns the run group; " +
                 "poll get_test_run for progress. The suite must belong to the current project. NOTE: a run " +
                 "makes real LLM calls and incurs cost.")]
    /// <summary>
    /// Starts the test run.
    /// </summary>
    public async Task<TestRunGroupDto> StartTestRun(
        [Description("The suite id (GUID) to run, from list_suites.")] Guid suiteId,
        CancellationToken cancellationToken)
    {
        project.RequireWriteScope();
        var p = await project.GetProjectAsync(cancellationToken);
        var suite = await suites.FindAsync(suiteId, cancellationToken);
        if (suite is null || suite.Agent.Project.Id != p.Id)
            throw new McpException($"Suite '{suiteId}' was not found in this project.");

        var group = await runner.RunInBackgroundAsync(
            suite, [suite.Agent.Endpoint], cancellationToken: cancellationToken);
        audit.LogAudit(AuditAction.TestRunStarted, nameof(ITestRunGroup), group.Id, suite.Name, projectId: p.Id);
        return await ToDtoAsync(group, cancellationToken);
    }

    /// <summary>
    /// Cancels the test run.
    /// </summary>
    [McpServerTool(Name = "cancel_test_run")]
    [Description("Cancel an in-progress test run (run group). The run must belong to the current project.")]
    public async Task<TestRunGroupDto> CancelTestRun(
        [Description("The run group id (GUID) to cancel, from list_test_runs.")] Guid runGroupId,
        CancellationToken cancellationToken)
    {
        project.RequireWriteScope();
        var group = await RequireGroupAsync(runGroupId, cancellationToken);
        var cancelled = await runner.CancelAsync(group, cancellationToken);
        return await ToDtoAsync(cancelled, cancellationToken);
    }

    [McpServerTool(Name = "get_run_failures")]
    [Description("Analyze the failing cases of a single run, with each evaluator's verdict and reasoning and " +
                 "the actual response — the primary evidence for an optimization theory. Pass a run id from a " +
                 "run group's `runs` (get_test_run). The run must belong to the current project.")]
    /// <summary>
    /// Gets the run failures.
    /// </summary>
    public async Task<IReadOnlyList<McpRunFailureDto>> GetRunFailures(
        [Description("The run id (GUID) — a single run from a run group's `runs`, via get_test_run.")] Guid runId,
        [Description("Maximum number of failing cases to return (1-50, default 20).")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var run = await RequireRunAsync(runId, cancellationToken);
        limit = Math.Clamp(limit, 1, 50);
        return run.TestResults
            .Where(r => !r.Passed)
            .Take(limit)
            .Select(r => new McpRunFailureDto(
                r.TestCase.Id,
                Truncate(r.ActualResponse.GetText(), 800),
                r.OverallScore?.ToString(),
                r.Evaluations
                    .Select(e => new McpEvaluationDto(e.Evaluator.Name, e.Passed, e.Reasoning, e.Score?.ToString()))
                    .ToArray()))
            .ToArray();
    }

    [McpServerTool(Name = "compare_runs")]
    [Description("Compare two runs of the same suite case-by-case: which cases were fixed (fail→pass), which " +
                 "regressed (pass→fail), and the pass rates. Both runs must belong to the current project.")]
    /// <summary>
    /// Compares the runs.
    /// </summary>
    public async Task<McpRunComparisonDto> CompareRuns(
        [Description("The baseline run id (GUID).")] Guid baselineRunId,
        [Description("The new run id (GUID) to compare against the baseline.")] Guid newRunId,
        CancellationToken cancellationToken)
    {
        var baseline = await RequireRunAsync(baselineRunId, cancellationToken);
        var candidate = await RequireRunAsync(newRunId, cancellationToken);

        var basePass = baseline.TestResults.ToDictionary(r => r.TestCase.Id, r => r.Passed);
        var newPass = candidate.TestResults.ToDictionary(r => r.TestCase.Id, r => r.Passed);
        var common = basePass.Keys.Where(newPass.ContainsKey).ToArray();
        var fixedCases = common.Where(id => !basePass[id] && newPass[id]).ToArray();
        var regressedCases = common.Where(id => basePass[id] && !newPass[id]).ToArray();

        return new McpRunComparisonDto(
            fixedCases.Length,
            regressedCases.Length,
            common.Length - fixedCases.Length - regressedCases.Length,
            fixedCases,
            regressedCases,
            PassRate(baseline),
            PassRate(candidate));
    }

    private static double PassRate(ITestRun run)
        => run.TestResults.Count == 0 ? 0 : run.TestResults.Count(r => r.Passed) / (double)run.TestResults.Count;

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private async Task<ITestRun> RequireRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var run = await runs.FindAsync(runId, cancellationToken);
        if (run is null || run.Group.Suite.Agent.Project.Id != p.Id)
            throw new McpException($"Run '{runId}' was not found in this project.");
        return run;
    }

    private async Task<ITestRunGroup> RequireGroupAsync(Guid runGroupId, CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var group = await groups.FindAsync(runGroupId, cancellationToken);
        if (group is null || group.Suite.Agent.Project.Id != p.Id)
            throw new McpException($"Test run '{runGroupId}' was not found in this project.");
        return group;
    }

    private async Task<TestRunGroupDto> ToDtoAsync(ITestRunGroup group, CancellationToken cancellationToken)
    {
        var groupRuns = await runs.GetByGroupAsync(group.Id, cancellationToken);
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
            Runs: groupRuns.Select(mapper.ToDto).ToArray(),
            CreatedAt: group.CreatedAt,
            UpdatedAt: group.UpdatedAt);
    }
}
