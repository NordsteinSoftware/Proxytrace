using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Api.Mcp;
using Proxytrace.Api.Mcp.Tools;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests.Mcp;

[TestClass]
public sealed class SuiteToolsTests : BaseTest<Module>
{
    private sealed class StubProjectAccessor : IMcpProjectAccessor
    {
        private readonly IProject project;

        public StubProjectAccessor(IProject project)
        {
            this.project = project;
        }

        public Task<IProject> GetProjectAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(project);

        // Write scope granted — these tests exercise the mutating tool's behavior, not the scope guard.
        public void RequireWriteScope()
        {
        }
    }

    [TestMethod]
    public async Task AddTraceToSuite_WithExpectedOutput_RecordsCorrectionAndKeepsSourceLink()
    {
        IServiceProvider services = GetServices();
        var (suite, call) = await SeedSuiteAndTraceAsync(services);
        var tools = BuildTools(services, call.Agent.Project);

        var result = await tools.AddTraceToSuite(suite.Id, call.Id, "The corrected answer", CancellationToken);

        var added = result.TestCases.Should().ContainSingle().Subject;
        added.SourceAgentCallId.Should().Be(call.Id);
        added.ExpectedOutput.Content.Should().Be("The corrected answer");
    }

    [TestMethod]
    public async Task AddTraceToSuite_WithoutExpectedOutput_PromotesAsIsWithSourceLink()
    {
        IServiceProvider services = GetServices();
        var (suite, call) = await SeedSuiteAndTraceAsync(services);
        var tools = BuildTools(services, call.Agent.Project);

        var result = await tools.AddTraceToSuite(suite.Id, call.Id, expectedOutput: null, CancellationToken);

        var added = result.TestCases.Should().ContainSingle().Subject;
        // Straight promotion: expected output is the recorded response, and the source link is preserved.
        added.SourceAgentCallId.Should().Be(call.Id);
        var response = call.Response ?? throw new InvalidOperationException("generated call must have a response");
        added.ExpectedOutput.Content.Should().Be(response.Response.GetText());
    }

    [TestMethod]
    public async Task AddTraceToSuite_CorrectingACompletedToolLoop_ReportsResolvedToolCalls()
    {
        // Promoting the LAST call of a tool loop yields a case whose input already contains the
        // harmful tool call AND its successful result. A run scores one model call, so no corrected
        // expected output contradicting that result can ever pass. The count is the caller's only
        // signal that it graded a summary rather than a decision — see docs/optimization-loop.md.
        IServiceProvider services = GetServices();
        var (suite, call) = await SeedSuiteAndTraceAsync(services);
        var summaryCall = await SeedCompletedToolLoopTraceAsync(services, call);
        var tools = BuildTools(services, call.Agent.Project);

        var result = await tools.AddTraceToSuite(
            suite.Id, summaryCall.Id, "I cannot refund an order outside the window.", CancellationToken);

        var added = result.TestCases.Should().ContainSingle().Subject;
        added.ResolvedToolCallCount.Should().Be(2);
    }

    [TestMethod]
    public async Task AddTraceToSuite_CorrectingADecisionPoint_ReportsNoResolvedToolCallForTheOpenDecision()
    {
        // The usable shape: the lookup came back, the decision is still the model's to make. Only
        // the answered lookup counts, and the case grades the decision itself.
        IServiceProvider services = GetServices();
        var (suite, call) = await SeedSuiteAndTraceAsync(services);
        var decisionCall = await SeedDecisionPointTraceAsync(services, call);
        var tools = BuildTools(services, call.Agent.Project);

        var result = await tools.AddTraceToSuite(
            suite.Id, decisionCall.Id, "I cannot refund an order outside the window.", CancellationToken);

        var added = result.TestCases.Should().ContainSingle().Subject;
        added.ResolvedToolCallCount.Should().Be(1);
    }

    /// <summary>
    /// A trace captured at the closing call of a tool loop: both tool calls made and both answered.
    /// </summary>
    private static Task<IAgentCall> SeedCompletedToolLoopTraceAsync(IServiceProvider services, IAgentCall template)
        => SeedTraceWithRequestAsync(services, template, new Conversation(
        [
            new UserMessage([Content.FromText("Maria promised me a refund.")]),
            new AssistantMessage([], [new ToolRequest("call-1", "start_return", "{}")]),
            new ToolMessage(new ToolResponse("call-1", [Content.FromText("""{"error":"return_not_available"}""")], true, null)),
            new AssistantMessage([], [new ToolRequest("call-2", "issue_refund", "{}")]),
            new ToolMessage(new ToolResponse("call-2", [Content.FromText("""{"refund_id":"REF-1"}""")], true, null))
        ]));

    /// <summary>
    /// A trace captured where the agent still has to decide: the lookup came back, nothing acted on yet.
    /// </summary>
    private static Task<IAgentCall> SeedDecisionPointTraceAsync(IServiceProvider services, IAgentCall template)
        => SeedTraceWithRequestAsync(services, template, new Conversation(
        [
            new UserMessage([Content.FromText("Maria promised me a refund.")]),
            new AssistantMessage([], [new ToolRequest("call-1", "lookup_order", "{}")]),
            new ToolMessage(new ToolResponse("call-1", [Content.FromText("""{"delivered_days_ago":45}""")], true, null))
        ]));

    private static async Task<IAgentCall> SeedTraceWithRequestAsync(
        IServiceProvider services,
        IAgentCall template,
        Conversation request)
    {
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        return await services.GetRequiredService<IAgentCallRepository>().AddAsync(
            createCall(template.Agent, template.Version, template.Endpoint, request, template.Response));
    }

    private static async Task<(ITestSuite Suite, IAgentCall Call)> SeedSuiteAndTraceAsync(IServiceProvider services)
    {
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync();
        var createEvaluator = services.GetRequiredService<IExactMatchEvaluator.CreateNew>();
        var evaluator = await services.GetRequiredService<IEvaluatorRepository>()
            .AddAsync(createEvaluator(call.Agent.Project));
        var createSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var suite = await services.GetRequiredService<ITestSuiteRepository>()
            .AddAsync(createSuite("Suite", call.Agent, [evaluator], []));
        return (suite, call);
    }

    private static SuiteTools BuildTools(IServiceProvider services, IProject project)
        => new(
            new StubProjectAccessor(project),
            services.GetRequiredService<ITestSuiteRepository>(),
            services.GetRequiredService<IAgentRepository>(),
            services.GetRequiredService<IAgentCallRepository>(),
            services.GetRequiredService<ITestCaseRepository>(),
            services.GetRequiredService<IEvaluatorRepository>(),
            services.GetRequiredService<ITestCase.CreateNewFromCall>(),
            services.GetRequiredService<ITestCase.CreateCorrection>(),
            services.GetRequiredService<IExactMatchEvaluator.CreateNew>(),
            services.GetRequiredService<ITestSuite.CreateNew>(),
            services.GetRequiredService<ITestSuite.CreateExisting>(),
            services.GetRequiredService<TestSuiteDtoMapper>(),
            services.GetRequiredService<ILicenseService>(),
            NullLogger<Audit>.Instance);
}
