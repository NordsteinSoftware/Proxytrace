using Nordstein.Core.AI.Clients;
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
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Tools;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Optimization;

/// <summary>
/// Integration tests for <see cref="UpdateToolDefinitionOptimizer"/> using a real LLM backend.
/// Skipped when appsettings.local.json is not configured.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class UpdateToolDefinitionOptimizerIntegrationTests : BaseTest<Module>
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
    /// Scenario: Agent has a "search" tool with a vague description.
    /// Tests fail because the model doesn't understand what arguments to pass.
    /// The optimizer should refine the tool description and parameter schemas.
    /// </summary>
    [TestMethod]
    public async Task SearchTool_VagueDescription_ProposesRefinedDefinition()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You are a search assistant. Use the search tool to find information.",
            tools:
            [
                new ToolSpecification(
                    "search",
                    "Searches for stuff",
                    ToolArguments.FromJsonSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "q": { "type": "string" }
                      },
                      "required": ["q"]
                    }
                    """)),
            ],
            failingCases:
            [
                new TestCaseData(
                    Input: "Find me recent articles about climate change",
                    Expected: """{"tool_call": "search", "args": {"q": "climate change recent articles"}}""",
                    Actual: """{"tool_call": "search", "args": {"q": "stuff"}}""",
                    FailReason: "Tool usage score 2/5: query parameter is too vague, tool description doesn't guide the model to formulate specific queries"),
                new TestCaseData(
                    Input: "Search for Python tutorials for beginners",
                    Expected: """{"tool_call": "search", "args": {"q": "Python tutorials beginners"}}""",
                    Actual: """{"tool_call": "search", "args": {"q": "python"}}""",
                    FailReason: "Tool usage score 2/5: query parameter too short, tool description doesn't specify to include key terms"),
            ],
            passingCases:
            [
                new TestCaseData(
                    Input: "Search for OpenAI",
                    Expected: """{"tool_call": "search", "args": {"q": "OpenAI"}}""",
                    Actual: """{"tool_call": "search", "args": {"q": "OpenAI"}}""",
                    FailReason: null),
            ]);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        IOptimizationTheory proposal = theories[0];
        proposal.Should().BeAssignableTo<IToolUpdateTheory>();

        var toolProposal = (IToolUpdateTheory)proposal;
        toolProposal.ProposedTools.Should().HaveCount(1);

        var proposedTool = toolProposal.ProposedTools[0];
        proposedTool.Name.Should().Be("search");
        proposedTool.Description.Length.Should().BeGreaterThan("Searches for stuff".Length);
        proposal.Rationale.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Scenario: Agent has a "create_ticket" tool whose parameter description is missing.
    /// The model doesn't know what format to use for priority/category fields.
    /// </summary>
    [TestMethod]
    public async Task CreateTicketTool_MissingParameterDescriptions_ProposesDetailedSchema()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You are a helpdesk bot. Create support tickets for user issues.",
            tools:
            [
                new ToolSpecification(
                    "create_ticket",
                    "Creates a ticket",
                    ToolArguments.FromJsonSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "title": { "type": "string" },
                        "priority": { "type": "string" },
                        "category": { "type": "string" }
                      },
                      "required": ["title", "priority"]
                    }
                    """)),
            ],
            failingCases:
            [
                new TestCaseData(
                    Input: "My laptop screen is broken, this is urgent!",
                    Expected: """{"tool_call": "create_ticket", "args": {"title": "Broken laptop screen", "priority": "high", "category": "hardware"}}""",
                    Actual: """{"tool_call": "create_ticket", "args": {"title": "laptop broken", "priority": "urgent", "category": "other"}}""",
                    FailReason: "Tool usage score 3/5: priority should be 'high' not 'urgent' (valid values: low/medium/high/critical); category should be 'hardware'"),
                new TestCaseData(
                    Input: "I can't log into my email",
                    Expected: """{"tool_call": "create_ticket", "args": {"title": "Email login failure", "priority": "medium", "category": "access"}}""",
                    Actual: """{"tool_call": "create_ticket", "args": {"title": "login issue", "priority": "normal", "category": "email"}}""",
                    FailReason: "Tool usage score 2/5: priority 'normal' is not a valid value; category 'email' is not in allowed set [hardware, software, access, network]"),
            ],
            passingCases: []);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        var toolProposal = (IToolUpdateTheory)theories[0];
        var proposedTool = toolProposal.ProposedTools[0];
        proposedTool.Name.Should().Be("create_ticket");

        string description = proposedTool.Description.ToLowerInvariant();
        string schema = proposedTool.Arguments.JsonSchema.ToLowerInvariant();
        (description.Contains("priority") || description.Contains("high") || description.Contains("low") ||
         schema.Contains("enum") || schema.Contains("high") || schema.Contains("low") ||
         schema.Contains("description"))
            .Should().BeTrue("Expected refined tool to clarify valid values for priority/category");
    }

    /// <summary>
    /// Scenario: Agent has a "send_email" tool but its description doesn't clarify format
    /// constraints, causing the model to generate invalid email bodies.
    /// </summary>
    [TestMethod]
    public async Task SendEmailTool_UnclearFormat_ProposesImprovedDescription()
    {
        var services = GetServices();
        var configuration = services.GetService<OptimizerTestConfiguration>();
        if (configuration is null)
            Assert.Inconclusive("No LLM configuration in appsettings.local.json");

        var (optimizer, group, cohort) = BuildScenario(
            services,
            agentSystemPrompt: "You are an email assistant. Compose and send emails for the user.",
            tools:
            [
                new ToolSpecification(
                    "send_email",
                    "Sends an email",
                    ToolArguments.FromJsonSchema("""
                    {
                      "type": "object",
                      "properties": {
                        "to": { "type": "string" },
                        "subject": { "type": "string" },
                        "body": { "type": "string" }
                      },
                      "required": ["to", "subject", "body"]
                    }
                    """)),
            ],
            failingCases:
            [
                new TestCaseData(
                    Input: "Send a meeting reminder to team@company.com for tomorrow at 3pm",
                    Expected: """{"tool_call": "send_email", "args": {"to": "team@company.com", "subject": "Meeting Reminder", "body": "Hi team,\n\nThis is a reminder about our meeting tomorrow at 3:00 PM.\n\nBest regards"}}""",
                    Actual: """{"tool_call": "send_email", "args": {"to": "team@company.com", "subject": "reminder", "body": "meeting tomorrow 3pm"}}""",
                    FailReason: "Tool usage score 2/5: subject not capitalized, body is too terse - expected professional email format with greeting and sign-off"),
                new TestCaseData(
                    Input: "Email john@test.com to say thanks for the presentation",
                    Expected: """{"tool_call": "send_email", "args": {"to": "john@test.com", "subject": "Thank You for the Presentation", "body": "Hi John,\n\nThank you for the great presentation today.\n\nBest regards"}}""",
                    Actual: """{"tool_call": "send_email", "args": {"to": "john@test.com", "subject": "thanks", "body": "thanks for the presentation"}}""",
                    FailReason: "Tool usage score 2/5: body is not formatted as a professional email"),
            ],
            passingCases: []);

        var theories = await optimizer.DiscoverTheories(group, [cohort], CancellationToken);

        theories.Should().ContainSingle();
        var toolProposal = (IToolUpdateTheory)theories[0];
        var proposedTool = toolProposal.ProposedTools[0];
        proposedTool.Name.Should().Be("send_email");
        proposedTool.Description.Length.Should().BeGreaterThan("Sends an email".Length);
    }

    private (UpdateToolDefinitionOptimizer Optimizer, ITestRunGroup Group, RunCohort Cohort) BuildScenario(
        IServiceProvider services,
        string agentSystemPrompt,
        IReadOnlyList<ToolSpecification> tools,
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
        agent.Tools.Returns(tools);
        agent.ModelParameters.Returns(Substitute.For<Nordstein.Core.AI.Completions.IModelParameters>());

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
            .OfType<UpdateToolDefinitionOptimizer>()
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
        evaluation.Score.Returns(passed ? EvaluationScore.Excellent : EvaluationScore.Bad);
        evaluation.Passed.Returns(passed);
        evaluation.Reasoning.Returns(data.FailReason ?? "matched");

        var result = Substitute.For<ITestResult>();
        result.Id.Returns(Guid.NewGuid());
        result.Passed.Returns(passed);
        result.OverallScore.Returns(passed ? EvaluationScore.Excellent : EvaluationScore.Bad);
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
