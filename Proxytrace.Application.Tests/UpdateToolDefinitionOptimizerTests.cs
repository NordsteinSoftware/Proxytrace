using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Application.Optimization.Internal.Evidence;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Tools;
using Nordstein.Core.AI.Serialization;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class UpdateToolDefinitionOptimizerTests : BaseTest<Module>
{
    private const string OnlyToolName = "search";

    private const string ValidJsonResponse = """
        {
          "tools": [
            {
              "name": "search",
              "description": "Refined search description.",
              "jsonSchema": "{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\",\"description\":\"the new query\"}},\"required\":[\"query\"]}"
            }
          ],
          "rationale": "Tightened the search description."
        }
        """;

    [TestMethod]
    public async Task DiscoverTheories_AgentHasNoTools_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse, includeTool: false);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 1,
            total: 1,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DiscoverTheories_NoRunForCurrentEndpoint_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse);
        RunCohort cohort = fixture.CreateRun(
            endpointId: Guid.NewGuid(),
            failed: 1,
            total: 1,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DiscoverTheories_ZeroFailures_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 0,
            total: 5,
            results: []);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DiscoverTheories_HappyPath_ProducesToolProposal()
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 1,
            total: 4,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().HaveCount(1);
        IOptimizationTheory theory = theories[0];
        theory.Should().BeAssignableTo<IToolUpdateTheory>();
        theory.Source.Should().Be(TheorySource.Optimizer);

        var tools = ((IToolUpdateTheory)theory).ProposedTools.ToList();
        tools.Should().HaveCount(1);
        tools[0].Name.Should().Be(OnlyToolName);
        tools[0].Description.Should().Be("Refined search description.");
        tools[0].Arguments.Count.Should().Be(1);
        tools[0].Arguments[0].Name.Should().Be("query");
    }

    [TestMethod]
    public async Task DiscoverTheories_LlmRenamesTool_ReturnsEmpty()
    {
        const string renamedJson = """
            {
              "tools": [
                {
                  "name": "different_name",
                  "description": "...",
                  "jsonSchema": "{\"type\":\"object\",\"properties\":{}}"
                }
              ],
              "rationale": "..."
            }
            """;
        OptimizerFixture fixture = BuildFixture(renamedJson);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 1,
            total: 1,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DiscoverTheories_LlmReturnsWrongToolCount_ReturnsEmpty()
    {
        const string wrongCountJson = """
            { "tools": [], "rationale": "..." }
            """;
        OptimizerFixture fixture = BuildFixture(wrongCountJson);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 1,
            total: 1,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    [TestMethod]
    public async Task DiscoverTheories_MalformedJsonResponse_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(cannedResponse: "this is not JSON");
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: 1,
            total: 1,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().BeEmpty();
    }

    private OptimizerFixture BuildFixture(string cannedResponse, bool includeTool = true)
    {
        IServiceProvider services = GetServices();
        return OptimizerFixture.Build(services, cannedResponse, includeTool);
    }

    private sealed class OptimizerFixture
    {
        public required UpdateToolDefinitionOptimizer Optimizer { get; init; }
        public required ITestRunGroup Group { get; init; }
        public required Guid AgentEndpointId { get; init; }

        // Builds a single-sample cohort for the endpoint: the optimizer now reads the cohort's
        // aggregated stats (one sample ⇒ those stats) rather than querying a stats reader.
        public RunCohort CreateRun(
            Guid endpointId,
            int failed,
            int total,
            IReadOnlyList<ITestResult> results)
        {
            var endpoint = Substitute.For<IModelEndpoint>();
            endpoint.Id.Returns(endpointId);

            var run = Substitute.For<ITestRun>();
            Guid runId = Guid.NewGuid();
            run.Id.Returns(runId);
            run.Endpoint.Returns(endpoint);
            run.TestResults.Returns(results);
            run.Group.Returns(Group);

            TestRunStats stats = new(
                TestRunId: runId,
                AgentId: Guid.Empty,
                EndpointId: endpointId,
                GroupId: Group.Id,
                SuiteId: Guid.Empty,
                TestCases: total,
                Passed: total - failed,
                TotalDuration: null,
                Usage: null,
                Cost: null,
                RunCompletedAt: DateTimeOffset.UtcNow);
            return RunCohort.Build([run], new Dictionary<Guid, TestRunStats> { [runId] = stats })[0];
        }

        public ITestResult CreateFailingResult()
        {
            var input = Conversation.Create()
                .With(new UserMessage([Content.FromText("search please")]));
            var expected = new AssistantMessage([Content.FromText("ok")], []);
            var actual = new AssistantMessage([Content.FromText("err")], []);

            var testCase = Substitute.For<ITestCase>();
            testCase.Input.Returns(input);
            testCase.ExpectedOutput.Returns(expected);

            var evaluator = Substitute.For<IEvaluator>();
            evaluator.Kind.Returns(EvaluatorKind.ExactMatch);

            var evaluation = Substitute.For<IEvaluation>();
            evaluation.Evaluator.Returns(evaluator);
            evaluation.Score.Returns(EvaluationScore.Terrible);
            evaluation.Passed.Returns(false);
            evaluation.Reasoning.Returns("tool failed");

            var result = Substitute.For<ITestResult>();
            result.Id.Returns(Guid.NewGuid());
            result.Passed.Returns(false);
            result.OverallScore.Returns(EvaluationScore.Terrible);
            result.TestCase.Returns(testCase);
            result.ActualResponse.Returns(actual);
            result.Evaluations.Returns([evaluation]);
            return result;
        }

        public static OptimizerFixture Build(IServiceProvider services, string cannedResponse, bool includeTool = true)
        {
            var theoryFactory = services.GetRequiredService<IToolUpdateTheory.CreateNew>();
            var outputFormatFactory = services.GetRequiredService<IOutputFormat.Create>();

            var agentEndpointId = Guid.NewGuid();
            var systemEndpoint = Substitute.For<IModelEndpoint>();
            systemEndpoint.Id.Returns(Guid.NewGuid());

            var agentEndpoint = Substitute.For<IModelEndpoint>();
            agentEndpoint.Id.Returns(agentEndpointId);

            var project = Substitute.For<IProject>();
            project.Id.Returns(Guid.NewGuid());
            project.SystemEndpoint.Returns(systemEndpoint);

            var systemPrompt = Substitute.For<IPromptTemplate>();
            systemPrompt.Template.Returns("You are an assistant.");
            systemPrompt.Variables.Returns([]);

            var initialTools = includeTool
                ? new List<ToolSpecification>
                {
                    new(OnlyToolName, "Original description.", ToolArguments.None),
                }
                : new List<ToolSpecification>();

            var agent = Substitute.For<IAgent>();
            agent.Id.Returns(Guid.NewGuid());
            agent.Name.Returns("Test Agent");
            agent.Endpoint.Returns(agentEndpoint);
            agent.Project.Returns(project);
            agent.SystemPrompt.Returns(systemPrompt);
            agent.Tools.Returns(initialTools);
            agent.ModelParameters.Returns(Substitute.For<Nordstein.Core.AI.Completions.IModelParameters>());

            var suite = Substitute.For<ITestSuite>();
            suite.Agent.Returns(agent);

            var group = Substitute.For<ITestRunGroup>();
            group.Id.Returns(Guid.NewGuid());
            group.Suite.Returns(suite);

            var systemAgent = new CannedJsonAgent(cannedResponse, outputFormatFactory);

            var prompts = Substitute.For<IPromptTemplateRepository>();
            prompts.GetAsync(UpdateToolDefinitionOptimizer.PromptName, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(systemPrompt));

            var agents = Substitute.For<IAgentRepository>();
            agents.GetOrCreateAsync(
                    Arg.Any<IPromptTemplate>(),
                    Arg.Any<IReadOnlyList<ToolSpecification>>(),
                    Arg.Any<IProject>(),
                    Arg.Any<IModelEndpoint>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool>(),
                    Arg.Any<Nordstein.Core.AI.Completions.IModelParameters?>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IAgent>(systemAgent));

            var optimizer = new UpdateToolDefinitionOptimizer(
                theoryFactory,
                prompts,
                agents,
                new OptimizerEvidenceBuilder());

            return new OptimizerFixture
            {
                Optimizer = optimizer,
                Group = group,
                AgentEndpointId = agentEndpointId,
            };
        }
    }
}
