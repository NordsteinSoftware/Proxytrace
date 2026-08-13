using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Application.Optimization.Internal.Evidence;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Tools;
using Proxytrace.Serialization;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class UpdateSystemPromptOptimizerTests : BaseTest<Module>
{
    private const string ValidJsonResponse = """
        {
          "proposedSystemPrompt": "You are an even better assistant.",
          "rationale": "Failing cases lacked structured guidance."
        }
        """;

    [TestMethod]
    public async Task DiscoverTheories_NoRunForCurrentEndpoint_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse);
        RunCohort cohortForOtherEndpoint = fixture.CreateRun(
            endpointId: Guid.NewGuid(),
            failed: 1,
            total: 1,
            results: []);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohortForOtherEndpoint],
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
    public async Task DiscoverTheories_HappyPath_ProducesSystemPromptProposal()
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
        theory.Should().BeAssignableTo<ISystemPromptTheory>();
        ((ISystemPromptTheory)theory).ProposedSystemMessage
            .Should().Be("You are an even better assistant.");
        theory.Source.Should().Be(TheorySource.Optimizer);
        theory.Rationale.Should().Contain("Failing cases lacked structured guidance.");
        theory.EvidenceTestRunIds.Should().ContainSingle().Which.Should().Be(cohort.Representative.Id);
    }

    [TestMethod]
    [DataRow(1, 20, Priority.Low)]      // 5%
    [DataRow(3, 20, Priority.Medium)]   // 15%
    [DataRow(6, 20, Priority.High)]     // 30%
    [DataRow(12, 20, Priority.Critical)] // 60%
    public async Task DiscoverTheories_PriorityBuckets(int failed, int total, Priority expected)
    {
        OptimizerFixture fixture = BuildFixture(ValidJsonResponse);
        RunCohort cohort = fixture.CreateRun(
            endpointId: fixture.AgentEndpointId,
            failed: failed,
            total: total,
            results: [fixture.CreateFailingResult()]);

        var theories = await fixture.Optimizer.DiscoverTheories(
            fixture.Group,
            [cohort],
            CancellationToken);

        theories.Should().ContainSingle()
            .Which.Priority.Should().Be(expected);
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

    [TestMethod]
    public async Task DiscoverTheories_EmptyProposedPrompt_ReturnsEmpty()
    {
        OptimizerFixture fixture = BuildFixture(
            """{ "proposedSystemPrompt": "", "rationale": "blank" }""");
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

    private OptimizerFixture BuildFixture(string cannedResponse)
    {
        IServiceProvider services = GetServices();
        return OptimizerFixture.Build(services, cannedResponse);
    }

    private sealed class OptimizerFixture
    {
        public required UpdateSystemPromptOptimizer Optimizer { get; init; }
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
                .With(new UserMessage([Content.FromText("ping")]));
            var expected = new AssistantMessage([Content.FromText("pong")], []);
            var actual = new AssistantMessage([Content.FromText("nope")], []);

            var testCase = Substitute.For<ITestCase>();
            testCase.Input.Returns(input);
            testCase.ExpectedOutput.Returns(expected);

            var evaluator = Substitute.For<IEvaluator>();
            evaluator.Kind.Returns(EvaluatorKind.ExactMatch);

            var evaluation = Substitute.For<IEvaluation>();
            evaluation.Evaluator.Returns(evaluator);
            evaluation.Score.Returns(EvaluationScore.Terrible);
            evaluation.Passed.Returns(false);
            evaluation.Reasoning.Returns("did not match");

            var result = Substitute.For<ITestResult>();
            result.Id.Returns(Guid.NewGuid());
            result.Passed.Returns(false);
            result.OverallScore.Returns(EvaluationScore.Terrible);
            result.TestCase.Returns(testCase);
            result.ActualResponse.Returns(actual);
            result.Evaluations.Returns([evaluation]);
            return result;
        }

        public static OptimizerFixture Build(IServiceProvider services, string cannedResponse)
        {
            var theoryFactory = services.GetRequiredService<ISystemPromptTheory.CreateNew>();
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

            var agent = Substitute.For<IAgent>();
            agent.Id.Returns(Guid.NewGuid());
            agent.Name.Returns("Test Agent");
            agent.Endpoint.Returns(agentEndpoint);
            agent.Project.Returns(project);
            agent.SystemPrompt.Returns(systemPrompt);
            agent.Tools.Returns(new List<ToolSpecification>());
            agent.ModelParameters.Returns(Substitute.For<Domain.Inference.IModelParameters>());

            var suite = Substitute.For<ITestSuite>();
            suite.Agent.Returns(agent);

            var group = Substitute.For<ITestRunGroup>();
            group.Id.Returns(Guid.NewGuid());
            group.Suite.Returns(suite);

            var systemAgent = new CannedJsonAgent(cannedResponse, outputFormatFactory);

            var prompts = Substitute.For<IPromptTemplateRepository>();
            prompts.GetAsync(UpdateSystemPromptOptimizer.PromptName, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(systemPrompt));

            var agents = Substitute.For<IAgentRepository>();
            agents.GetOrCreateAsync(
                    Arg.Any<IPromptTemplate>(),
                    Arg.Any<IReadOnlyList<ToolSpecification>>(),
                    Arg.Any<IProject>(),
                    Arg.Any<IModelEndpoint>(),
                    Arg.Any<string?>(),
                    Arg.Any<bool>(),
                    Arg.Any<Domain.Inference.IModelParameters?>(),
                    Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IAgent>(systemAgent));

            var optimizer = new UpdateSystemPromptOptimizer(
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
