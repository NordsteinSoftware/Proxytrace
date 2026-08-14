using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class EvaluatorTestBenchControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Load_WhenEvaluatorScoredTheResult_ReturnsLoggedEvaluation()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        var controller = ResolveController(services);

        var result = await controller.Load(evaluator.Id, testCase.Id, CancellationToken);

        var payload = result.Value;
        payload.Should().NotBeNull();
        payload.LoggedEvaluation.Should().NotBeNull();
        payload.LoggedEvaluation.EvaluatorId.Should().Be(evaluator.Id);
        payload.LoggedEvaluation.Score.Should().Be(EvaluationScore.Good);
    }

    [TestMethod]
    public async Task Load_WhenEvaluatorDidNotScoreTheResult_ReturnsNullLoggedEvaluation()
    {
        IServiceProvider services = GetServices();
        var scorer = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var other = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, scorer, EvaluationScore.Good);
        var controller = ResolveController(services);

        var result = await controller.Load(other.Id, testCase.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.LoggedEvaluation.Should().BeNull();
    }

    [TestMethod]
    public async Task Load_WhenActualResponseIsToolCallOnly_RendersToolCallInsteadOfEmpty()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var testCase = await services.GetRequiredService<IDomainEntityGenerator<ITestCase>>().CreateAsync(CancellationToken);
        var toolResponse = new AssistantMessage(
            [],
            [new ToolRequest("call-1", "get_weather", """{"city":"NYC"}""")]);
        var completion = services.GetRequiredService<ICompletion.Create>()(toolResponse, null, TimeSpan.FromMilliseconds(10));
        var evaluation = services.GetRequiredService<IEvaluation.Create>()(evaluator, EvaluationScore.Good, TimeSpan.FromMilliseconds(10), null, null, null);
        var result = services.GetRequiredService<ITestResult.CreateNew>()(testCase, completion, [evaluation]);
        await services.GetRequiredService<ITestResultRepository>().AddAsync(result, CancellationToken);
        var controller = ResolveController(services);

        var payload = (await controller.Load(evaluator.Id, testCase.Id, CancellationToken)).Value;

        payload.Should().NotBeNull();
        payload.ActualResponse.Should().NotBeNullOrWhiteSpace();
        payload.ActualResponse.Should().Contain("[tool call]");
        payload.ActualResponse.Should().Contain("get_weather");
    }

    [TestMethod]
    public async Task Recent_ReturnsItemsWithTheEvaluatorsLoggedScore()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Bad);
        var controller = ResolveController(services);

        var result = await controller.Recent(evaluator.Id, 10, CancellationToken);

        result.Value.Should().ContainSingle();
        result.Value[0].TestCaseId.Should().Be(testCase.Id);
        result.Value[0].Score.Should().Be(EvaluationScore.Bad);
    }

    [TestMethod]
    public async Task Search_WhenReasoningMatchesQuery_ReturnsOnlyTheScopedMatch()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (matched, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good, "needle phrase");
        await SeedScoredResult(services, evaluator, EvaluationScore.Bad, "unrelated text");
        var controller = ResolveController(services);

        var result = await controller.Search(evaluator.Id, "needle", 10, CancellationToken);

        result.Value.Should().ContainSingle();
        result.Value[0].TestCaseId.Should().Be(matched.Id);
        result.Value[0].Score.Should().Be(EvaluationScore.Good);
    }

    [TestMethod]
    public async Task Search_WhenEvaluatorMissing_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Search(Guid.NewGuid(), "anything", 10, CancellationToken);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    private async Task<(ITestCase TestCase, ITestResult Result)> SeedScoredResult(
        IServiceProvider services, IEvaluator evaluator, EvaluationScore score, string? reasoning = null)
    {
        var testCase = await services.GetRequiredService<IDomainEntityGenerator<ITestCase>>().CreateAsync(CancellationToken);
        var completion = await services.GetRequiredService<IDomainObjectGenerator<ICompletion>>().CreateAsync(CancellationToken);
        var evaluation = services.GetRequiredService<IEvaluation.Create>()(evaluator, score, TimeSpan.FromMilliseconds(10), null, null, reasoning);
        var result = services.GetRequiredService<ITestResult.CreateNew>()(testCase, completion, [evaluation]);
        await services.GetRequiredService<ITestResultRepository>().AddAsync(result, CancellationToken);
        return (testCase, result);
    }

    [TestMethod]
    public async Task Run_WhenEvaluatorInaccessible_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        var controller = ResolveController(services, DenyingGuard());

        var result = await controller.Run(evaluator.Id, new(testCase.Id, null), CancellationToken);

        // The evaluator exists, but the caller is not a member of its project → hidden behind a 404.
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task Load_WhenEvaluatorInaccessible_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        var controller = ResolveController(services, DenyingGuard());

        var result = await controller.Load(evaluator.Id, testCase.Id, CancellationToken);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task Load_WhenTestCaseBelongsToInaccessibleProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        // Seed a result so the test case has loadable content; without the guard Load would return it.
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        // The test case is owned by a suite in a *different* project (cross-tenant).
        await SeedSuiteContainingTestCaseInForeignProject(services, testCase);
        var evaluatorProjectId = await services.GetRequiredService<IEvaluatorRepository>()
            .GetProjectIdAsync(evaluator.Id, CancellationToken);
        evaluatorProjectId.Should().NotBeNull();
        var controller = ResolveController(services, GuardAllowingOnly(evaluatorProjectId.Value));

        var result = await controller.Load(evaluator.Id, testCase.Id, CancellationToken);

        // The caller may access the evaluator's project but not the test case's project → hidden behind
        // a 404, and the foreign test-case content is never returned.
        result.Result.Should().BeOfType<NotFoundObjectResult>();
        result.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task Run_WhenTestCaseBelongsToInaccessibleProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        await SeedSuiteContainingTestCaseInForeignProject(services, testCase);
        var evaluatorProjectId = await services.GetRequiredService<IEvaluatorRepository>()
            .GetProjectIdAsync(evaluator.Id, CancellationToken);
        evaluatorProjectId.Should().NotBeNull();
        var controller = ResolveController(services, GuardAllowingOnly(evaluatorProjectId.Value));

        var result = await controller.Run(evaluator.Id, new(testCase.Id, null), CancellationToken);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        result.Value.Should().BeNull();
    }

    [TestMethod]
    public async Task Load_WhenTestCaseInAccessibleProject_ReturnsPayload()
    {
        IServiceProvider services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);
        var (testCase, _) = await SeedScoredResult(services, evaluator, EvaluationScore.Good);
        // A generated agent reuses the same (first) project as the evaluator, so the suite that
        // references the test case resolves to the evaluator's own, accessible project.
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var suite = services.GetRequiredService<ITestSuite.CreateNew>()("Same-project suite", agent, [], [testCase]);
        await services.GetRequiredService<ITestSuiteRepository>().AddAsync(suite, CancellationToken);
        var evaluatorProjectId = await services.GetRequiredService<IEvaluatorRepository>()
            .GetProjectIdAsync(evaluator.Id, CancellationToken);
        evaluatorProjectId.Should().NotBeNull();
        var controller = ResolveController(services, GuardAllowingOnly(evaluatorProjectId.Value));

        var result = await controller.Load(evaluator.Id, testCase.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.TestCaseId.Should().Be(testCase.Id);
    }

    // Creates a fresh project + agent + suite that references the given test case, so the test case
    // resolves (via the suite -> agent -> project chain) to a project distinct from the shared one the
    // generators reuse. Returns the foreign project id.
    private async Task<Guid> SeedSuiteContainingTestCaseInForeignProject(IServiceProvider services, ITestCase testCase)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var prompt = await services.GetRequiredService<IDomainObjectGenerator<IPromptTemplate>>().CreateAsync(CancellationToken);
        var modelParameters = await services.GetRequiredService<IDomainObjectGenerator<IModelParameters>>().CreateAsync(CancellationToken);
        var agent = await services.GetRequiredService<IAgentRepository>().CreateWithInitialVersionAsync(
            name: "Foreign agent",
            systemPrompt: prompt,
            tools: [],
            project: project,
            endpoint: endpoint,
            modelParameters: modelParameters,
            isSystemAgent: false,
            cancellationToken: CancellationToken);
        var suite = services.GetRequiredService<ITestSuite.CreateNew>()("Foreign suite", agent, [], [testCase]);
        await services.GetRequiredService<ITestSuiteRepository>().AddAsync(suite, CancellationToken);
        return project.Id;
    }

    // Non-admin member of exactly one project: may access that project, denied every other.
    private static Proxytrace.Api.Auth.IProjectAccessGuard GuardAllowingOnly(Guid projectId)
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.CanAccessProjectAsync(projectId, Arg.Any<CancellationToken>()).Returns(true);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([projectId]));
        return guard;
    }

    // Non-admin member of nothing: denied every project.
    private static Proxytrace.Api.Auth.IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    private static EvaluatorTestBenchController ResolveController(IServiceProvider services)
        => ResolveController(services, services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static EvaluatorTestBenchController ResolveController(
        IServiceProvider services, Proxytrace.Api.Auth.IProjectAccessGuard guard) => new(
        services.GetRequiredService<IEvaluatorRepository>(),
        services.GetRequiredService<ITestCaseRepository>(),
        services.GetRequiredService<ITestResultRepository>(),
        services.GetRequiredService<ITestSuiteRepository>(),
        services.GetRequiredService<ICompletion.Create>(),
        services.GetRequiredService<ITestResult.CreateNew>(),
        guard);
}
