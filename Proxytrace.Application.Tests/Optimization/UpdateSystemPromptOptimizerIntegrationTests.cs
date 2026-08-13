using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Optimization.Internal;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
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
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// Integration tests for <see cref="UpdateSystemPromptOptimizer"/> using a real LLM backend.
/// Skipped when appsettings.local.json is not configured.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class UpdateSystemPromptOptimizerIntegrationTests : BaseTest<Module>
{
    private readonly IStatsReader<TestRunStats, TestRunStats.Filter> statsReader =
        Substitute.For<IStatsReader<TestRunStats, TestRunStats.Filter>>();

    private readonly ITestRunnerService testRunnerService = Substitute.For<ITestRunnerService>();

    protected override void ConfigureContainer(ContainerBuilder builder)
    {
        base.ConfigureContainer(builder);
        var configuration = OptimizerTestConfiguration.TryLoad();
        if (configuration != null)
        {
            builder.RegisterInstance(configuration);

            var endpoint = Substitute.For<IModelEndpoint>();
            var model = Substitute.For<IModel>();
            model.Id.Returns(Guid.NewGuid());
            model.Name.Returns(configuration.Model);

            var provider = Substitute.For<IModelProvider>();
            provider.Name.Returns("Test");
            provider.Id.Returns(Guid.NewGuid());
            provider.ApiKey.Returns(configuration.ApiKey);
            provider.Endpoint.Returns(new Uri(configuration.Endpoint));
            provider.Kind.Returns(ModelProviderKind.OpenAiCompatible);

            endpoint.Id.Returns(Guid.NewGuid());
            endpoint.Model.Returns(model);
            endpoint.Provider.Returns(provider);

            builder.RegisterInstance(endpoint);

            var project = Substitute.For<IProject>();
            project.Name.Returns("Test Project");
            project.Id.Returns(Guid.NewGuid());
            project.SystemEndpoint.Returns(endpoint);
            project.Members.Returns(Array.Empty<Domain.User.IUser>());
            builder.RegisterInstance(project);

            builder.RegisterBuildCallback(async c =>
            {
                var models = c.Resolve<IRepository<IModel>>();
                await models.AddAsync(model);

                var modelproviders = c.Resolve<IRepository<IModelProvider>>();
                await modelproviders.AddAsync(provider);

                var endpoints = c.Resolve<IRepository<IModelEndpoint>>();
                await endpoints.AddAsync(endpoint);

                var projects = c.Resolve<IRepository<IProject>>();
                await projects.AddAsync(project);
            });
        }

        builder.RegisterInstance(statsReader)
            .As<IStatsReader<TestRunStats, TestRunStats.Filter>>();

        builder.RegisterInstance(testRunnerService)
            .As<ITestRunnerService>();

        builder.RegisterInstance(new KioskOptions()).AsSelf().SingleInstance();
        builder.RegisterType<Proxytrace.Infrastructure.Internal.ModelClient>()
            .As<IModelClient>()
            .AsSelf();
    }

    /// <summary>
    /// Scenario: Agent is a math tutor but its system prompt doesn't tell it to show work.
    /// Test cases fail because the evaluator expects step-by-step solutions.
    /// The optimizer should propose a prompt that instructs the agent to show work.
    /// </summary>
    [TestMethod]
    public async Task MathTutor_MissingShowWorkInstruction_ProposesImprovedPrompt()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You are a math tutor. Answer math questions.",
            failingCases:
            [
                new TestCaseData(
                    Input: "What is 15% of 240?",
                    Expected: "15% of 240 = 0.15 × 240 = 36",
                    Actual: "36",
                    FailReason: "Expected step-by-step working, got only final answer"),
                new TestCaseData(
                    Input: "Solve 2x + 5 = 13",
                    Expected: "2x + 5 = 13 → 2x = 8 → x = 4",
                    Actual: "x = 4",
                    FailReason: "Expected step-by-step working, got only final answer"),
                new TestCaseData(
                    Input: "Calculate the area of a circle with radius 7",
                    Expected: "A = π × r² = π × 49 ≈ 153.94",
                    Actual: "153.94",
                    FailReason: "Expected formula and intermediate steps"),
            ],
            passingCases:
            [
                new TestCaseData(
                    Input: "What is 2 + 2?",
                    Expected: "4",
                    Actual: "4",
                    FailReason: null),
            ]);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        IOptimizationTheory proposal = theories[0];
        proposal.Should().BeAssignableTo<ISystemPromptTheory>();

        var promptProposal = (ISystemPromptTheory)proposal;
        promptProposal.ProposedSystemMessage.Should().NotBeNullOrWhiteSpace();
        promptProposal.ProposedSystemMessage.Length.Should().BeGreaterThan(20);
        proposal.Rationale.Should().NotBeNullOrWhiteSpace();
        proposal.EvidenceTestRunIds.Should().NotBeEmpty();
    }

    /// <summary>
    /// Scenario: Agent is a JSON formatter but doesn't produce valid JSON.
    /// The optimizer should add formatting instructions to the prompt.
    /// </summary>
    [TestMethod]
    public async Task JsonFormatter_InvalidOutput_ProposesStructuredFormatting()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You convert user requests into data.",
            failingCases:
            [
                new TestCaseData(
                    Input: "Create a user profile for John, age 30, from NYC",
                    Expected: """{"name": "John", "age": 30, "city": "NYC"}""",
                    Actual: "Name: John, Age: 30, City: NYC",
                    FailReason: "Output is not valid JSON"),
                new TestCaseData(
                    Input: "Create a product entry: Widget, $9.99, in stock",
                    Expected: """{"product": "Widget", "price": 9.99, "inStock": true}""",
                    Actual: "Product: Widget\nPrice: $9.99\nIn Stock: Yes",
                    FailReason: "Output is not valid JSON"),
            ],
            passingCases: []);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        var promptProposal = (ISystemPromptTheory)theories[0];
        promptProposal.ProposedSystemMessage.ToLowerInvariant().Should().Contain("json");
    }

    /// <summary>
    /// Scenario: A customer support agent fails because it doesn't greet the user politely.
    /// </summary>
    [TestMethod]
    public async Task SupportAgent_MissingGreeting_ProposesPolitePrompt()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You are a support agent. Help users with their issues.",
            failingCases:
            [
                new TestCaseData(
                    Input: "My order hasn't arrived yet",
                    Expected: "Hello! I'm sorry to hear about the delay. Let me look into your order for you.",
                    Actual: "What is your order number?",
                    FailReason: "Politeness score 2/5: response is curt, lacks greeting or empathy"),
                new TestCaseData(
                    Input: "I want a refund",
                    Expected: "Hi there! I understand your frustration. I'd be happy to help you with a refund.",
                    Actual: "Provide your order ID for a refund.",
                    FailReason: "Politeness score 2/5: no greeting, no empathy, too terse"),
            ],
            passingCases:
            [
                new TestCaseData(
                    Input: "Thanks for your help!",
                    Expected: "You're welcome! Is there anything else I can help with?",
                    Actual: "You're welcome! Let me know if you need anything else.",
                    FailReason: null),
            ]);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        var promptProposal = (ISystemPromptTheory)theories[0];
        promptProposal.ProposedSystemMessage.Should().NotBeNullOrWhiteSpace();
        string lower = promptProposal.ProposedSystemMessage.ToLowerInvariant();
        (lower.Contains("greet") || lower.Contains("polite") || lower.Contains("empathy") ||
         lower.Contains("friendly") || lower.Contains("warm") || lower.Contains("hello"))
            .Should().BeTrue($"Expected prompt to address politeness, got: {promptProposal.ProposedSystemMessage}");
    }

    private (UpdateSystemPromptOptimizer Optimizer, ITestRunGroup Group, RunCohort Cohort) BuildScenario(
        IServiceProvider services,
        string agentSystemPrompt,
        IReadOnlyList<TestCaseData> failingCases,
        IReadOnlyList<TestCaseData> passingCases)
    {
        var project = services.GetRequiredService<IProject>();
        var endpoint = services.GetRequiredService<IModelEndpoint>();

        var systemPrompt = Substitute.For<IPromptTemplate>();
        systemPrompt.Template.Returns(agentSystemPrompt);
        systemPrompt.Variables.Returns([]);

        var agent = Substitute.For<IAgent>();
        agent.Id.Returns(Guid.NewGuid());
        agent.Name.Returns("Test Agent");
        agent.Endpoint.Returns(endpoint);
        agent.Project.Returns(project);
        agent.SystemPrompt.Returns(systemPrompt);
        agent.Tools.Returns(new List<ToolSpecification>());
        agent.ModelParameters.Returns(Substitute.For<Domain.Inference.IModelParameters>());

        var suite = Substitute.For<ITestSuite>();
        suite.Agent.Returns(agent);

        var group = Substitute.For<ITestRunGroup>();
        group.Id.Returns(Guid.NewGuid());
        group.Suite.Returns(suite);

        var results = new List<ITestResult>();
        foreach (var tc in failingCases) results.Add(CreateResult(tc, passed: false));
        foreach (var tc in passingCases) results.Add(CreateResult(tc, passed: true));

        var runId = Guid.NewGuid();
        var run = Substitute.For<ITestRun>();
        run.Id.Returns(runId);
        run.Endpoint.Returns(endpoint);
        run.TestResults.Returns(results);
        run.Group.Returns(group);

        // Configure stats for this run
        var stats = new TestRunStats(
            TestRunId: runId,
            AgentId: Guid.Empty,
            EndpointId: endpoint.Id,
            GroupId: group.Id,
            SuiteId: Guid.Empty,
            TestCases: failingCases.Count + passingCases.Count,
            Passed: passingCases.Count,
            TotalDuration: null,
            Usage: null,
            Cost: null,
            RunCompletedAt: DateTimeOffset.UtcNow);

        statsReader.FindAsync(runId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TestRunStats?>(stats));

        // Configure AB test runner to return a dummy run
        var abTestRun = Substitute.For<ITestRun>();
        abTestRun.Id.Returns(Guid.NewGuid());
        var abGroup = Substitute.For<ITestRunGroup>();
        abGroup.GetTestRuns(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ITestRun>>([abTestRun]));

        testRunnerService.RunInForegroundAsync(
                Arg.Any<ITestSuite>(),
                Arg.Any<IReadOnlyList<IModelEndpoint>>(),
                Arg.Any<IAgent?>(),
                Arg.Any<bool>(),
                Arg.Any<Func<ITestRunGroup, CancellationToken, Task>?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(abGroup));

        var optimizer = services.GetRequiredService<IEnumerable<IOptimizerImplementation>>()
            .OfType<UpdateSystemPromptOptimizer>()
            .Single();

        // The optimizer consumes cohorts; this scenario is a single sample, so its aggregated stats
        // equal the run's stats above.
        var cohort = RunCohort.Build([run], new Dictionary<Guid, TestRunStats> { [runId] = stats })[0];
        return (optimizer, group, cohort);
    }

    private static ITestResult CreateResult(TestCaseData data, bool passed)
    {
        var input = Conversation.Create()
            .With(new UserMessage([Content.FromText(data.Input)]));
        var expected = new AssistantMessage([Content.FromText(data.Expected)], []);
        var actual = new AssistantMessage([Content.FromText(data.Actual)], []);

        var testCase = Substitute.For<ITestCase>();
        testCase.Input.Returns(input);
        testCase.ExpectedOutput.Returns(expected);

        var evaluator = Substitute.For<IEvaluator>();
        evaluator.Kind.Returns(EvaluatorKind.ExactMatch);

        var evaluation = Substitute.For<IEvaluation>();
        evaluation.Evaluator.Returns(evaluator);
        evaluation.Score.Returns(passed ? EvaluationScore.Excellent : EvaluationScore.Terrible);
        evaluation.Passed.Returns(passed);
        evaluation.Reasoning.Returns(data.FailReason ?? "matched");

        var result = Substitute.For<ITestResult>();
        result.Id.Returns(Guid.NewGuid());
        result.Passed.Returns(passed);
        result.OverallScore.Returns(passed ? EvaluationScore.Excellent : EvaluationScore.Terrible);
        result.TestCase.Returns(testCase);
        result.ActualResponse.Returns(actual);
        result.Evaluations.Returns([evaluation]);
        return result;
    }

    internal sealed record TestCaseData(
        string Input,
        string Expected,
        string Actual,
        string? FailReason);
}