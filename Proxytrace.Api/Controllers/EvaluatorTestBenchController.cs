using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Evaluators;
using Proxytrace.Api.Dto.TestRuns;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/evaluators/{evaluatorId:guid}/test-bench")]
public class EvaluatorTestBenchController : ControllerBase
{
    private readonly IEvaluatorRepository evaluators;
    private readonly ITestCaseRepository testCases;
    private readonly ITestResultRepository testResults;
    private readonly ITestSuiteRepository testSuites;
    private readonly ICompletion.Create createCompletion;
    private readonly ITestResult.CreateNew createTestResult;
    private readonly IProjectAccessGuard accessGuard;

    public EvaluatorTestBenchController(
        IEvaluatorRepository evaluators,
        ITestCaseRepository testCases,
        ITestResultRepository testResults,
        ITestSuiteRepository testSuites,
        ICompletion.Create createCompletion,
        ITestResult.CreateNew createTestResult,
        IProjectAccessGuard accessGuard)
    {
        this.evaluators = evaluators;
        this.testCases = testCases;
        this.testResults = testResults;
        this.testSuites = testSuites;
        this.createCompletion = createCompletion;
        this.createTestResult = createTestResult;
        this.accessGuard = accessGuard;
    }

    // The test bench reads and evaluates against an evaluator's project data — and `run` spends the
    // project's provider credential. Resolve the evaluator's project and hide it behind a 404 when the
    // caller is not a member, both to deny access and to avoid an existence oracle.
    private async Task<bool> CanAccessEvaluatorAsync(Guid evaluatorId, CancellationToken cancellationToken)
    {
        var projectId = await evaluators.GetProjectIdAsync(evaluatorId, cancellationToken);
        return projectId is not null && await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken);
    }

    // `load`/`run` also accept a caller-supplied test-case id that is independent of the evaluator. A
    // test case carries no project of its own, so resolve it through the suite that references it and
    // deny it behind the same 404 when it resolves to a project the caller cannot access — otherwise a
    // member of one project could read another project's test-case conversation, expected/actual
    // responses, and scores (#265). A test case in no suite has no resolvable owner, so it is left
    // accessible (e.g. a result whose suite was deleted remains loadable by its evaluator's members).
    private async Task<bool> CanAccessTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken)
    {
        var projectId = await testSuites.GetProjectIdByTestCaseAsync(testCaseId, cancellationToken);
        return projectId is null || await accessGuard.CanAccessProjectAsync(projectId.Value, cancellationToken);
    }

    [HttpGet("load")]
    public async Task<ActionResult<EvaluatorTestBenchPayloadDto>> Load(
        Guid evaluatorId,
        [FromQuery] Guid testCaseId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEvaluatorAsync(evaluatorId, cancellationToken))
            return NotFound($"Evaluator {evaluatorId} not found.");
        if (!await CanAccessTestCaseAsync(testCaseId, cancellationToken))
            return NotFound($"Test case {testCaseId} not found.");
        var testCase = await testCases.FindAsync(testCaseId, cancellationToken);
        if (testCase is null)
            return NotFound($"Test case {testCaseId} not found.");

        var latest = await testResults.GetLatestByTestCaseAsync(testCaseId, cancellationToken);
        if (latest is null)
            return NotFound($"No test result exists for test case {testCaseId}.");

        var logged = latest.Evaluations.FirstOrDefault(e => e.Evaluator.Id == evaluatorId);

        return new EvaluatorTestBenchPayloadDto(
            SourceTestResultId: latest.Id,
            TestCaseId: testCase.Id,
            TestCaseSummary: testCase.GetSummary(),
            Conversation: testCase.Input.Messages
                .Select(m => new TestRunMessageDto(m.Role.ToString().ToLowerInvariant(), m.GetText()))
                .ToArray(),
            ExpectedResponse: testCase.ExpectedOutput.GetDisplayText(),
            ActualResponse: latest.ActualResponse.GetDisplayText(),
            LoggedEvaluation: logged is null
                ? null
                : new EvaluationResultDto(
                    logged.Evaluator.Id,
                    logged.Evaluator.Kind,
                    logged.Evaluator.Name,
                    logged.Score,
                    logged.Reasoning,
                    logged.ErrorMessage));
    }

    [HttpGet("default")]
    public async Task<ActionResult<EvaluatorTestBenchDefaultDto>> Default(
        Guid evaluatorId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEvaluatorAsync(evaluatorId, cancellationToken))
            return NotFound($"Evaluator {evaluatorId} not found.");

        var latest = await testResults.GetLatestByEvaluatorAsync(evaluatorId, cancellationToken);
        return latest is null
            ? new EvaluatorTestBenchDefaultDto(null, null) 
            : new EvaluatorTestBenchDefaultDto(latest.TestCase.Id, latest.TestCase.GetSummary());
    }

    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<EvaluatorTestBenchRecentItemDto>>> Recent(
        Guid evaluatorId,
        [FromQuery] int count,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEvaluatorAsync(evaluatorId, cancellationToken))
            return NotFound($"Evaluator {evaluatorId} not found.");

        var capped = Math.Clamp(count, 1, 50);
        var recent = await testResults.GetRecentByEvaluatorAsync(evaluatorId, capped, cancellationToken: cancellationToken);
        return recent
            .Select(r => new EvaluatorTestBenchRecentItemDto(
                r.TestCase.Id,
                r.TestCase.GetSummary(),
                r.Evaluations.FirstOrDefault(e => e.Evaluator.Id == evaluatorId)?.Score))
            .ToArray();
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<EvaluatorTestBenchRecentItemDto>>> Search(
        Guid evaluatorId,
        [FromQuery] string? q,
        [FromQuery] int count,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEvaluatorAsync(evaluatorId, cancellationToken))
            return NotFound($"Evaluator {evaluatorId} not found.");

        var capped = Math.Clamp(count, 1, 50);
        var matches = await testResults.SearchByEvaluatorAsync(evaluatorId, q ?? string.Empty, capped, cancellationToken);
        return matches
            .Select(r => new EvaluatorTestBenchRecentItemDto(
                r.TestCase.Id,
                r.TestCase.GetSummary(),
                r.Evaluations.FirstOrDefault(e => e.Evaluator.Id == evaluatorId)?.Score))
            .ToArray();
    }

    [HttpPost("run")]
    public async Task<ActionResult<EvaluationResultDto>> Run(
        Guid evaluatorId,
        [FromBody] RunEvaluatorOnBenchRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEvaluatorAsync(evaluatorId, cancellationToken))
            return NotFound($"Evaluator {evaluatorId} not found.");
        if (!await CanAccessTestCaseAsync(request.TestCaseId, cancellationToken))
            return NotFound($"Test case {request.TestCaseId} not found.");
        var evaluator = await evaluators.FindAsync(evaluatorId, cancellationToken);
        if (evaluator is null)
            return NotFound($"Evaluator {evaluatorId} not found.");
        var testCase = await testCases.FindAsync(request.TestCaseId, cancellationToken);
        if (testCase is null)
            return NotFound($"Test case {request.TestCaseId} not found.");

        AssistantMessage actual;
        TimeSpan latency;
        if (request.ActualResponseOverride is not null)
        {
            actual = new AssistantMessage([Nordstein.Core.AI.Messages.Content.FromText(request.ActualResponseOverride)], []);
            var latest = await testResults.GetLatestByTestCaseAsync(request.TestCaseId, cancellationToken);
            latency = latest?.Latency ?? TimeSpan.FromMilliseconds(1);
        }
        else
        {
            var latest = await testResults.GetLatestByTestCaseAsync(request.TestCaseId, cancellationToken);
            if (latest is null)
                return NotFound($"No test result exists for test case {request.TestCaseId}.");
            actual = latest.ActualResponse;
            latency = latest.Latency;
        }

        var completion = createCompletion(actual, null, latency);
        var transient = createTestResult(testCase, completion, []);

        var evaluation = await evaluator.EvaluateAsync(transient, cancellationToken);
        if (evaluation is null)
            return UnprocessableEntity($"Evaluator returned no evaluation.");

        return new EvaluationResultDto(
            evaluator.Id,
            evaluator.Kind,
            evaluator.Name,
            evaluation.Score,
            evaluation.Reasoning,
            evaluation.ErrorMessage);
    }

}
