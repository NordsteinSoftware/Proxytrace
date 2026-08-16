using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// MCP tools for listing test suites and curating them from captured traces. All operations are scoped
/// to the current project (the project the API key belongs to).
/// </summary>
[McpServerToolType]
internal sealed class SuiteTools
{
    private readonly IMcpProjectAccessor project;
    private readonly ITestSuiteRepository suites;
    private readonly IAgentRepository agents;
    private readonly IAgentCallRepository calls;
    private readonly ITestCaseRepository testCases;
    private readonly IEvaluatorRepository evaluators;
    private readonly ITestCase.CreateNewFromCall createTestCaseFromCall;
    private readonly ITestCase.CreateCorrection createTestCaseCorrection;
    private readonly IExactMatchEvaluator.CreateNew createEvaluator;
    private readonly ITestSuite.CreateNew createSuite;
    private readonly ITestSuite.CreateExisting createSuiteExisting;
    private readonly TestSuiteDtoMapper mapper;
    private readonly ILicenseService license;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuiteTools"/> class.
    /// </summary>
    public SuiteTools(
        IMcpProjectAccessor project,
        ITestSuiteRepository suites,
        IAgentRepository agents,
        IAgentCallRepository calls,
        ITestCaseRepository testCases,
        IEvaluatorRepository evaluators,
        ITestCase.CreateNewFromCall createTestCaseFromCall,
        ITestCase.CreateCorrection createTestCaseCorrection,
        IExactMatchEvaluator.CreateNew createEvaluator,
        ITestSuite.CreateNew createSuite,
        ITestSuite.CreateExisting createSuiteExisting,
        TestSuiteDtoMapper mapper,
        ILicenseService license,
        ILogger<Audit> audit)
    {
        this.project = project;
        this.suites = suites;
        this.agents = agents;
        this.calls = calls;
        this.testCases = testCases;
        this.evaluators = evaluators;
        this.createTestCaseFromCall = createTestCaseFromCall;
        this.createTestCaseCorrection = createTestCaseCorrection;
        this.createEvaluator = createEvaluator;
        this.createSuite = createSuite;
        this.createSuiteExisting = createSuiteExisting;
        this.mapper = mapper;
        this.license = license;
        this.audit = audit;
    }

    /// <summary>
    /// Lists the suites.
    /// </summary>
    [McpServerTool(Name = "list_suites")]
    [Description("List the test suites in the current project, with each suite's test-case count.")]
    public async Task<IReadOnlyList<TestSuiteListItemDto>> ListSuites(
        [Description("Maximum number of suites to return (1-100, default 50).")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        var paged = await suites.GetByProjectPagedAsync(p.Id, 1, limit, cancellationToken);
        return paged.Items.Select(s => mapper.ToListItemDto(s, [])).ToArray();
    }

    [McpServerTool(Name = "get_suite")]
    [Description("Get a single test suite by id, including its test cases and evaluators. " +
                 "The suite must belong to the current project.")]
    /// <summary>
    /// Gets the suite.
    /// </summary>
    public async Task<TestSuiteDto> GetSuite(
        [Description("The suite id (GUID), as returned by list_suites.")] Guid suiteId,
        CancellationToken cancellationToken)
    {
        var suite = await RequireSuiteAsync(suiteId, cancellationToken);
        return mapper.ToDto(suite);
    }

    [McpServerTool(Name = "create_suite_from_traces")]
    [Description("Create a benchmark test suite by promoting captured traces: each trace becomes a test " +
                 "case whose expected output is the response recorded during that call. The agent must " +
                 "belong to the current project. A default exact-match evaluator is attached.")]
    /// <summary>
    /// Creates the suite from traces.
    /// </summary>
    public async Task<TestSuiteDto> CreateSuiteFromTraces(
        [Description("Name for the new suite.")] string name,
        [Description("The agent id (GUID) the suite benchmarks, from list_agents.")] Guid agentId,
        [Description("Trace ids (GUIDs) to promote into test cases, from list_traces.")] Guid[] traceIds,
        CancellationToken cancellationToken)
    {
        project.RequireWriteScope();
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("A suite name is required.");
        if (traceIds.Length == 0)
            throw new McpException("At least one trace id must be provided.");

        var p = await project.GetProjectAsync(cancellationToken);
        var agent = await agents.FindAsync(agentId, cancellationToken);
        if (agent is null || agent.Project.Id != p.Id)
            throw new McpException($"Agent '{agentId}' was not found in this project.");

        license.Ensure(LicenseLimit.MaxTestSuites, await suites.CountAsync(cancellationToken));

        var defaultEvaluator = await evaluators.AddAsync(createEvaluator(agent.Project), cancellationToken);

        var cases = new List<ITestCase>();
        foreach (var traceId in traceIds.Distinct())
        {
            var call = await calls.FindAsync(traceId, cancellationToken);
            if (call is null || call.Agent.Project.Id != p.Id)
                throw new McpException($"Trace '{traceId}' was not found in this project.");
            if (call.Response is null)
                throw new McpException($"Trace '{traceId}' has no response and cannot be promoted to a test case.");

            var saved = await testCases.AddAsync(createTestCaseFromCall(call), cancellationToken);
            cases.Add(saved);
        }

        var suite = await suites.AddAsync(createSuite(name, agent, [defaultEvaluator], cases), cancellationToken);
        audit.LogAudit(AuditAction.TestSuiteCreated, nameof(ITestSuite), suite.Id, suite.Name, projectId: p.Id);
        return mapper.ToDto(suite);
    }

    [McpServerTool(Name = "add_trace_to_suite")]
    [Description("Add a captured trace to an existing test suite as a new test case. By default the " +
                 "expected output is the response recorded during that call (a straight promotion). " +
                 "Pass expectedOutput to instead record a human correction — 'the agent saw this input, " +
                 "and the right answer was X' — which is what turns a rejected output into a regression " +
                 "test. Either way the case keeps a link back to the source trace. The suite and trace " +
                 "must belong to the project. IMPORTANT when correcting: a run scores exactly one " +
                 "model call, so pick the trace where the agent MADE the wrong decision, not the one " +
                 "that reports it. An agent turn that uses tools spans several traces; the last one " +
                 "already contains the harmful tool call and its result, so a corrected expected " +
                 "output contradicting that result can never pass. Check the new case's " +
                 "resolvedToolCallCount in the returned suite — anything above 0 means you graded a " +
                 "summary; correct the earlier trace whose response contains the wrong tool call.")]
    /// <summary>
    /// Adds the trace to suite.
    /// </summary>
    public async Task<TestSuiteDto> AddTraceToSuite(
        [Description("The suite id (GUID), from list_suites.")] Guid suiteId,
        [Description("The trace id (GUID) to add, from list_traces.")] Guid traceId,
        [Description("Optional corrected assistant answer. When provided, the trace's request becomes the " +
                     "input and this text becomes the expected output (a correction). When omitted, the " +
                     "trace is promoted as-is.")] string? expectedOutput = null,
        CancellationToken cancellationToken = default)
    {
        project.RequireWriteScope();
        var p = await project.GetProjectAsync(cancellationToken);
        var suite = await RequireSuiteAsync(suiteId, cancellationToken);

        var call = await calls.FindAsync(traceId, cancellationToken);
        if (call is null || call.Agent.Project.Id != p.Id)
            throw new McpException($"Trace '{traceId}' was not found in this project.");
        // A straight promotion needs the recorded response; a correction supplies its own expected output,
        // so only the promote-as-is path requires a response to exist.
        if (expectedOutput is null && call.Response is null)
            throw new McpException($"Trace '{traceId}' has no response and cannot be added as a test case.");

        var testCase = expectedOutput is not null
            ? createTestCaseCorrection(call, mapper.BuildAssistantMessage(new TestSuiteMessageDto("assistant", expectedOutput)))
            : createTestCaseFromCall(call);
        var saved = await testCases.AddAsync(testCase, cancellationToken);
        var updatedCases = suite.TestCases.Append(saved).ToArray();
        var updated = createSuiteExisting(suite.Name, suite.Agent, suite.Evaluators, updatedCases, suite);
        var savedSuite = await suites.UpdateAsync(updated, cancellationToken);
        audit.LogAudit(AuditAction.TestCaseCreated, nameof(ITestCase), saved.Id, projectId: p.Id);
        return mapper.ToDto(savedSuite);
    }

    private async Task<ITestSuite> RequireSuiteAsync(Guid suiteId, CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var suite = await suites.FindAsync(suiteId, cancellationToken);
        if (suite is null || suite.Agent.Project.Id != p.Id)
            throw new McpException($"Suite '{suiteId}' was not found in this project.");
        return suite;
    }
}
