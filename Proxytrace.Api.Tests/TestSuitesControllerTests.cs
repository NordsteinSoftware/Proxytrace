using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;
using Nordstein.Core.Testing;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestSuitesControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task PromoteFromTraces_WithEvaluatorIds_AttachesSelectedEvaluatorsAndNotDefault()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);
        var helpfulness = await services.GetRequiredService<IDomainEntityGenerator<IAgenticEvaluator>>().CreateAsync(CancellationToken);

        var request = new PromoteTracesRequest(
            Name: "From-traces helpfulness suite",
            AgentId: call.Agent.Id,
            AgentCallIds: [call.Id],
            EvaluatorIds: [helpfulness.Id]);

        var result = await controller.PromoteFromTraces(request, CancellationToken);

        var actionResult = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var dto = actionResult.Value as TestSuiteDto
            ?? throw new InvalidOperationException("Expected TestSuiteDto value.");
        dto.Evaluators.Should().HaveCount(1);
        dto.Evaluators.Single().Id.Should().Be(helpfulness.Id);
        dto.Evaluators.Single().Kind.Should().Be(EvaluatorKind.Agentic);
    }

    [TestMethod]
    public async Task PromoteFromTraces_WithoutEvaluatorIds_FallsBackToDefaultExactMatch()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var request = new PromoteTracesRequest(
            Name: "From-traces default suite",
            AgentId: call.Agent.Id,
            AgentCallIds: [call.Id]);

        var result = await controller.PromoteFromTraces(request, CancellationToken);

        var actionResult = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var dto = actionResult.Value as TestSuiteDto
            ?? throw new InvalidOperationException("Expected TestSuiteDto value.");
        dto.Evaluators.Should().ContainSingle(e => e.Kind == EvaluatorKind.ExactMatch);
    }

    [TestMethod]
    public async Task PromoteFromTraces_SetsSourceAgentCallIdOnEachCase()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var result = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "Provenance suite", AgentId: call.Agent.Id, AgentCallIds: [call.Id]),
            CancellationToken);

        var actionResult = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var dto = actionResult.Value as TestSuiteDto
            ?? throw new InvalidOperationException("Expected TestSuiteDto value.");
        // The promoted case keeps a link back to the trace it came from (the endpoint's doc-comment
        // promises this).
        dto.TestCases.Should().ContainSingle().Which.SourceAgentCallId.Should().Be(call.Id);
    }

    [TestMethod]
    public async Task AddTestCase_CorrectionFromTrace_PreservesSourceAndUsesCorrectedOutput()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);
        var promote = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "Correction suite", AgentId: call.Agent.Id, AgentCallIds: [call.Id]),
            CancellationToken);
        var promoteAction = (CreatedAtActionResult)(promote.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var suiteId = (Guid)((promoteAction.RouteValues ?? throw new InvalidOperationException("Expected route values."))["id"]
            ?? throw new InvalidOperationException("Expected 'id' route value."));

        // Record a correction against the same trace: input from the trace, a corrected expected answer.
        var correction = new TestSuiteMessageDto("assistant", "The corrected answer");
        var addResult = await controller.AddTestCase(
            suiteId,
            new AddTestCaseRequest(FromAgentCallId: call.Id, Input: null, ExpectedOutput: correction),
            CancellationToken);

        var dto = addResult.Value ?? throw new InvalidOperationException("Expected non-null Value.");
        var corrected = dto.TestCases.Should().HaveCount(2).And.Subject.Last();
        corrected.SourceAgentCallId.Should().Be(call.Id);
        corrected.ExpectedOutput.Content.Should().Be("The corrected answer");
    }

    [TestMethod]
    public async Task AddTestCase_AfterPromote_DoesNotDuplicateEvaluatorJunctionRows()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var generator = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var firstCall = await generator.CreateAsync(CancellationToken);
        var secondCall = await generator.CreateAsync(CancellationToken);
        var helpfulness = await services.GetRequiredService<IDomainEntityGenerator<IAgenticEvaluator>>().CreateAsync(CancellationToken);

        var promoteResult = await controller.PromoteFromTraces(
            new PromoteTracesRequest(
                Name: "Suite under test",
                AgentId: firstCall.Agent.Id,
                AgentCallIds: [firstCall.Id],
                EvaluatorIds: [helpfulness.Id]),
            CancellationToken);
        var promoteAction = (CreatedAtActionResult)(promoteResult.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var routeValues = promoteAction.RouteValues ?? throw new InvalidOperationException("Expected non-null RouteValues.");
        var suiteId = (Guid)(routeValues["id"] ?? throw new InvalidOperationException("Expected 'id' route value."));

        var addResult = await controller.AddTestCase(
            suiteId,
            new AddTestCaseRequest(FromAgentCallId: secondCall.Id, Input: null, ExpectedOutput: null),
            CancellationToken);

        var dto = addResult.Value
            ?? throw new InvalidOperationException("Expected non-null Value.");
        dto.TestCases.Should().HaveCount(2);
        dto.Evaluators.Should().ContainSingle(e => e.Id == helpfulness.Id);
    }

    [TestMethod]
    public async Task GetAll_NoFilter_ReturnsAllSuitesPaged()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var createSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var suiteRepo = services.GetRequiredService<ITestSuiteRepository>();
        await suiteRepo.AddAsync(createSuite("Suite A", agent, [], []), CancellationToken);
        await suiteRepo.AddAsync(createSuite("Suite B", agent, [], []), CancellationToken);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Total.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(50);
    }

    [TestMethod]
    public async Task GetAll_FilterByAgent_ReturnsOnlyAgentSuites()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var agentA = await agentGen.CreateAsync(CancellationToken);
        var agentB = await agentGen.CreateAsync(CancellationToken);
        var createSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var suiteRepo = services.GetRequiredService<ITestSuiteRepository>();
        await suiteRepo.AddAsync(createSuite("A1", agentA, [], []), CancellationToken);
        await suiteRepo.AddAsync(createSuite("A2", agentA, [], []), CancellationToken);
        await suiteRepo.AddAsync(createSuite("B1", agentB, [], []), CancellationToken);

        var result = await controller.GetAll(agentId: agentA.Id, cancellationToken: CancellationToken);

        result.Total.Should().Be(2);
        result.Items.Should().OnlyContain(s => s.AgentId == agentA.Id);
    }

    [TestMethod]
    public async Task GetAll_Pagination_RespectsPageAndPageSize()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var createSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var suiteRepo = services.GetRequiredService<ITestSuiteRepository>();
        for (int i = 0; i < 5; i++)
            await suiteRepo.AddAsync(createSuite($"Suite {i}", agent, [], []), CancellationToken);

        var page1 = await controller.GetAll(page: 1, pageSize: 2, cancellationToken: CancellationToken);
        var page3 = await controller.GetAll(page: 3, pageSize: 2, cancellationToken: CancellationToken);

        page1.Items.Should().HaveCount(2);
        page1.Total.Should().Be(5);
        page3.Items.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Get_ExistingSuite_ReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.Get(suite.Id, CancellationToken);

        var value = result.Value;
        value.Should().NotBeNull();
        value.Id.Should().Be(suite.Id);
        value.Name.Should().Be(suite.Name);
    }

    [TestMethod]
    public async Task Get_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Create_AgentNotFound_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var request = new CreateTestSuiteRequest(
            Name: "Suite",
            AgentId: Guid.NewGuid(),
            TestCases: []);

        var result = await controller.Create(request, CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_WithoutEvaluatorIds_AddsDefaultExactMatch()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var request = new CreateTestSuiteRequest(
            Name: "Default-evaluator suite",
            AgentId: agent.Id,
            TestCases: []);

        var result = await controller.Create(request, CancellationToken);

        var action = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var dto = (TestSuiteDto)(action.Value ?? throw new InvalidOperationException("Expected DTO."));
        dto.Evaluators.Should().ContainSingle(e => e.Kind == EvaluatorKind.ExactMatch);
    }

    [TestMethod]
    public async Task Create_TestCaseMissingFromCallAndInline_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var request = new CreateTestSuiteRequest(
            Name: "Bad suite",
            AgentId: agent.Id,
            TestCases: [new CreateTestCaseRequest(FromAgentCallId: null, Input: null, ExpectedOutput: null)]);

        var result = await controller.Create(request, CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_WithInlineTestCase_CreatesSuiteAndTestCase()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var request = new CreateTestSuiteRequest(
            Name: "Inline suite",
            AgentId: agent.Id,
            TestCases:
            [
                new CreateTestCaseRequest(
                    FromAgentCallId: null,
                    Input: [new TestSuiteMessageDto("user", "hi")],
                    ExpectedOutput: new TestSuiteMessageDto("assistant", "hello")),
            ]);

        var result = await controller.Create(request, CancellationToken);

        var action = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException("Expected non-null Result."));
        var dto = (TestSuiteDto)(action.Value ?? throw new InvalidOperationException("Expected DTO."));
        dto.TestCases.Should().ContainSingle();
        dto.TestCases[0].ExpectedOutput.Content.Should().Be("hello");
    }

    [TestMethod]
    public async Task Update_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateTestSuiteRequest(AgentId: null, EvaluatorIds: null, TestCaseIds: null),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_ChangeEvaluators_PersistsNewSet()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var newEval = await services.GetRequiredService<IDomainEntityGenerator<IAgenticEvaluator>>().CreateAsync(CancellationToken);

        var result = await controller.Update(
            suite.Id,
            new UpdateTestSuiteRequest(AgentId: null, EvaluatorIds: [newEval.Id], TestCaseIds: null),
            CancellationToken);

        var value = result.Value;
        value.Should().NotBeNull();
        value.Evaluators.Should().ContainSingle(e => e.Id == newEval.Id);
    }

    [TestMethod]
    public async Task Delete_ExistingSuite_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(suite.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await services.GetRequiredService<ITestSuiteRepository>().ContainsAsync(suite.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task Delete_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task PromoteFromTraces_AgentNotFound_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "x", AgentId: Guid.NewGuid(), AgentCallIds: [Guid.NewGuid()]),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task PromoteFromTraces_NoCallIds_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var result = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "x", AgentId: agent.Id, AgentCallIds: []),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task PromoteFromTraces_CallIdNotFound_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var result = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "x", AgentId: agent.Id, AgentCallIds: [Guid.NewGuid()]),
            CancellationToken);

        // A missing call and an inaccessible (foreign) call return the same 404, so a crafted id
        // cannot be used as a cross-tenant existence oracle.
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [TestMethod]
    public async Task PromoteFromTraces_CallHasNoResponse_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        // Borrow a fully-formed call to reuse its valid agent/version/endpoint/request graph, then
        // persist a sibling call that never received a response (e.g. the upstream errored).
        var seed = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var noResponseCall = createCall(
            seed.Agent, seed.Version, seed.Endpoint, seed.Request, null,
            System.Net.HttpStatusCode.InternalServerError, null, "upstream error");
        var saved = await services.GetRequiredService<IAgentCallRepository>().AddAsync(noResponseCall, CancellationToken);

        var result = await controller.PromoteFromTraces(
            new PromoteTracesRequest(Name: "No-response suite", AgentId: seed.Agent.Id, AgentCallIds: [saved.Id]),
            CancellationToken);

        // A response-less call is a client-input precondition: it must be a 400, not a generic 500
        // from a bare InvalidOperationException that no exception mapper handles.
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task AddTestCase_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.AddTestCase(
            Guid.NewGuid(),
            new AddTestCaseRequest(FromAgentCallId: null, Input: null, ExpectedOutput: null),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task AddTestCase_MissingFromCallAndInline_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.AddTestCase(
            suite.Id,
            new AddTestCaseRequest(FromAgentCallId: null, Input: null, ExpectedOutput: null),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task AddTestCase_InlineMessages_AppendsTestCase()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var initialCount = suite.TestCases.Count;

        var result = await controller.AddTestCase(
            suite.Id,
            new AddTestCaseRequest(
                FromAgentCallId: null,
                Input: [new TestSuiteMessageDto("user", "hello")],
                ExpectedOutput: new TestSuiteMessageDto("assistant", "world")),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.TestCases.Should().HaveCount(initialCount + 1);
    }

    [TestMethod]
    public async Task AddTestCase_FromCallWithExpectedOverride_UsesProvidedToolRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var result = await controller.AddTestCase(
            suite.Id,
            new AddTestCaseRequest(
                FromAgentCallId: call.Id,
                Input: null,
                ExpectedOutput: new TestSuiteMessageDto("assistant", "", [new ToolRequestInputDto("lookup", "{}")])),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.TestCases.Should()
            .ContainSingle(tc => tc.ExpectedOutput.ToolRequests != null && tc.ExpectedOutput.ToolRequests.Count == 1)
            .Which.ExpectedOutput.ToolRequests!.Single().Name.Should().Be("lookup");
    }

    [TestMethod]
    public async Task RemoveTestCase_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.RemoveTestCase(Guid.NewGuid(), Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task RemoveTestCase_DropsMatchingCase()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var targetCase = suite.TestCases.First();

        var result = await controller.RemoveTestCase(suite.Id, targetCase.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.TestCases.Should().NotContain(tc => tc.Id == targetCase.Id);
    }

    [TestMethod]
    public async Task GetAll_WithFinalizedRun_PopulatesSuiteRunStats()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();
        var groupId = Guid.NewGuid();
        await writer.UpsertAsync(
            new TestRunStats(
                TestRunId: Guid.NewGuid(),
                AgentId: suite.Agent.Id,
                EndpointId: Guid.NewGuid(),
                GroupId: groupId,
                SuiteId: suite.Id,
                TestCases: 4,
                Passed: 3,
                TotalDuration: TimeSpan.FromSeconds(2),
                Usage: null,
                Cost: null,
                RunCompletedAt: DateTimeOffset.UtcNow),
            CancellationToken);

        var paged = await controller.GetAll(cancellationToken: CancellationToken);

        var dto = paged.Items.Single(s => s.Id == suite.Id);
        dto.TotalRuns.Should().Be(1);
        dto.PassRate.Should().Be(75);
        dto.LastRunGroupId.Should().Be(groupId);
        dto.LastRunAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task GetRunStats_MissingSuite_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetRunStats(Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetRunStats_NoRuns_ReturnsZeroedStats()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.GetRunStats(suite.Id, cancellationToken: CancellationToken);

        var dto = result.Value ?? throw new InvalidOperationException("Expected DTO.");
        dto.RunCount.Should().Be(0);
        dto.PassRate.Should().BeNull();
        dto.AvgDurationMs.Should().BeNull();
        dto.TotalCost.Should().BeNull();
    }

    [TestMethod]
    public async Task GetRunStats_WithRuns_AggregatesPassRateDurationAndCost()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var writer = services.GetRequiredService<IStatsWriter<TestRunStats>>();

        await writer.UpsertAsync(new TestRunStats(
            TestRunId: Guid.NewGuid(), AgentId: suite.Agent.Id, EndpointId: Guid.NewGuid(),
            GroupId: Guid.NewGuid(), SuiteId: suite.Id, TestCases: 4, Passed: 3,
            TotalDuration: TimeSpan.FromSeconds(2), Usage: null, Cost: 0.10m,
            RunCompletedAt: DateTimeOffset.UtcNow), CancellationToken);
        await writer.UpsertAsync(new TestRunStats(
            TestRunId: Guid.NewGuid(), AgentId: suite.Agent.Id, EndpointId: Guid.NewGuid(),
            GroupId: Guid.NewGuid(), SuiteId: suite.Id, TestCases: 6, Passed: 6,
            TotalDuration: TimeSpan.FromSeconds(4), Usage: null, Cost: 0.20m,
            RunCompletedAt: DateTimeOffset.UtcNow), CancellationToken);

        var result = await controller.GetRunStats(suite.Id, cancellationToken: CancellationToken);

        var dto = result.Value ?? throw new InvalidOperationException("Expected DTO.");
        dto.RunCount.Should().Be(2);
        dto.PassRate.Should().Be(90);            // (3+6)/(4+6) = 0.9
        dto.AvgDurationMs.Should().Be(3000);     // (2000+4000)/2
        dto.TotalCost.Should().Be(0.30m);
    }

    private static TestSuitesController ResolveController(
        IServiceProvider services,
        ILicenseService? license = null,
        Proxytrace.Api.Auth.IProjectAccessGuard? accessGuard = null) =>
        new(
            services.GetRequiredService<ITestSuiteRepository>(),
            services.GetRequiredService<IAgentRepository>(),
            services.GetRequiredService<IAgentCallRepository>(),
            services.GetRequiredService<ITestCaseRepository>(),
            services.GetRequiredService<IEvaluatorRepository>(),
            services.GetRequiredService<ITestCase.CreateNew>(),
            services.GetRequiredService<ITestCase.CreateNewFromCall>(),
            services.GetRequiredService<ITestCase.CreateCorrection>(),
            services.GetRequiredService<IExactMatchEvaluator.CreateNew>(),
            services.GetRequiredService<ITestSuite.CreateNew>(),
            services.GetRequiredService<ITestSuite.CreateExisting>(),
            services.GetRequiredService<TestSuiteDtoMapper>(),
            services.GetRequiredService<IStatsReader<TestRunStats, TestRunStats.Filter>>(),
            license ?? UnlimitedLicense(),
            accessGuard ?? services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
            services.GetRequiredService<Nordstein.Core.Common.Async.IAsyncLock>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);

    [TestMethod]
    public async Task PromoteFromTraces_WhenTraceInForeignProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();

        // Create the trace first so it claims the generators' default project; then mint a *separate*
        // project and put the suite's agent there. The two now live in genuinely different projects.
        var foreignCall = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);
        var agent = await CreateAgentInFreshProject(services);

        // Caller is a member of the suite's agent project only — not the trace's project.
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.CanAccessProjectAsync(agent.Project.Id, Arg.Any<CancellationToken>()).Returns(true);
        var controller = ResolveController(services, accessGuard: guard);

        var request = new PromoteTracesRequest(
            Name: "Cross-tenant promote",
            AgentId: agent.Id,
            AgentCallIds: [foreignCall.Id]);

        var result = await controller.PromoteFromTraces(request, CancellationToken);

        // The foreign trace must not be copied into the caller's suite — hidden behind a 404.
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // The agent generators reuse one shared project (GetOrCreate), so build an agent in a brand-new
    // project directly through the repository to get a genuinely distinct tenant.
    private async Task<IAgent> CreateAgentInFreshProject(IServiceProvider services)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<Proxytrace.Domain.Project.IProject>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<Proxytrace.Domain.ModelEndpoint.IModelEndpoint>>().GetOrCreateAsync(CancellationToken);
        var prompt = await services.GetRequiredService<IDomainObjectGenerator<Proxytrace.Domain.Prompt.IPromptTemplate>>().CreateAsync(CancellationToken);
        var modelParameters = await services.GetRequiredService<IDomainObjectGenerator<Proxytrace.Domain.Inference.IModelParameters>>().CreateAsync(CancellationToken);
        return await services.GetRequiredService<IAgentRepository>().CreateWithInitialVersionAsync(
            name: "Suite agent",
            systemPrompt: prompt,
            tools: [],
            project: project,
            endpoint: endpoint,
            modelParameters: modelParameters,
            isSystemAgent: false,
            cancellationToken: CancellationToken);
    }

    private static ILicenseService UnlimitedLicense()
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(Arg.Any<LicenseLimit>()).Returns(long.MaxValue);
        return license;
    }

    private static ILicenseService LicenseWithSuiteLimit(long max)
    {
        var license = Substitute.For<ILicenseService>();
        license.GetLimit(Arg.Any<LicenseLimit>()).Returns(long.MaxValue);
        license.GetLimit(LicenseLimit.MaxTestSuites).Returns(max);
        return license;
    }

    [TestMethod]
    public async Task Create_WhenSuiteLimitReached_ThrowsLicenseLimitExceeded()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services, LicenseWithSuiteLimit(0));
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var request = new CreateTestSuiteRequest(Name: "Suite", AgentId: agent.Id, TestCases: []);

        await FluentActions
            .Invoking(() => controller.Create(request, CancellationToken))
            .Should().ThrowAsync<LicenseLimitExceededException>()
            .Where(e => e.Limit == LicenseLimit.MaxTestSuites);
    }

    [TestMethod]
    public async Task Create_WhenBelowSuiteLimit_Succeeds()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services, LicenseWithSuiteLimit(5));
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var request = new CreateTestSuiteRequest(Name: "Suite", AgentId: agent.Id, TestCases: []);

        var result = await controller.Create(request, CancellationToken);

        result.Result.Should().BeOfType<CreatedAtActionResult>();
    }

    [TestMethod]
    public async Task Create_WhenTwoRequestsRaceAtTheLimit_OnlyOneSucceeds()
    {
        // The count and the create it authorises are check-then-act. Concurrently, both requests
        // observed "0 suites" against a limit of 1 and both proceeded, leaving a Free-tier install
        // with two suites and no way to notice.
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var license = LicenseWithSuiteLimit(1);

        // Separate controller instances, as two concurrent requests would have. They share the
        // singleton IAsyncLock from the container, which is what actually serializes them.
        var first = ResolveController(services, license);
        var second = ResolveController(services, license);

        var results = await Task.WhenAll(
            Attempt(first, "Suite A"),
            Attempt(second, "Suite B"));

        results.Count(r => r).Should().Be(1, "exactly one of the two racing creates may be admitted");
        (await services.GetRequiredService<ITestSuiteRepository>().CountAsync(CancellationToken))
            .Should().Be(1, "the licensed limit must hold under concurrency");

        async Task<bool> Attempt(TestSuitesController controller, string name)
        {
            try
            {
                await controller.Create(
                    new CreateTestSuiteRequest(Name: name, AgentId: agent.Id, TestCases: []),
                    CancellationToken);
                return true;
            }
            catch (LicenseLimitExceededException)
            {
                return false;
            }
        }
    }

    [TestMethod]
    public async Task PromoteFromTraces_WhenSuiteLimitReached_ThrowsLicenseLimitExceeded()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services, LicenseWithSuiteLimit(0));
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var request = new PromoteTracesRequest(
            Name: "Promoted",
            AgentId: call.Agent.Id,
            AgentCallIds: [call.Id]);

        await FluentActions
            .Invoking(() => controller.PromoteFromTraces(request, CancellationToken))
            .Should().ThrowAsync<LicenseLimitExceededException>()
            .Where(e => e.Limit == LicenseLimit.MaxTestSuites);
    }
}
