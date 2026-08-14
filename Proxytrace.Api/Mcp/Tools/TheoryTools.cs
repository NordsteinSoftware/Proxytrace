using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.Theories;
using Proxytrace.Application.Optimization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Tools;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// The kind of change an optimization theory proposes. Exactly one applies per <c>submit_theory</c>.
/// </summary>
internal enum McpTheoryChangeKind
{
    SystemPrompt,
    ModelSwitch,
    ToolUpdate,
}

/// <summary>
/// MCP tools for the optimization-theory loop in the current project: read past theories and submit a
/// new one for background A/B validation. Gated by the <see cref="LicenseFeature.OptimizationProposals"/>
/// license feature.
/// </summary>
[McpServerToolType]
internal sealed class TheoryTools
{
    private readonly IMcpProjectAccessor project;
    private readonly IOptimizationTheoryRepository repository;
    private readonly IAgentRepository agents;
    private readonly IRepository<ITestSuite> suites;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly ITheoryValidationService validation;
    private readonly ISystemPromptTheory.CreateNew createSystemPrompt;
    private readonly IModelSwitchTheory.CreateNew createModelSwitch;
    private readonly IToolUpdateTheory.CreateNew createToolUpdate;
    private readonly TheoryDtoMapper mapper;
    private readonly ILicenseService license;
    private readonly ILogger<Audit> audit;

    public TheoryTools(
        IMcpProjectAccessor project,
        IOptimizationTheoryRepository repository,
        IAgentRepository agents,
        IRepository<ITestSuite> suites,
        IRepository<IModelEndpoint> endpoints,
        ITheoryValidationService validation,
        ISystemPromptTheory.CreateNew createSystemPrompt,
        IModelSwitchTheory.CreateNew createModelSwitch,
        IToolUpdateTheory.CreateNew createToolUpdate,
        TheoryDtoMapper mapper,
        ILicenseService license,
        ILogger<Audit> audit)
    {
        this.project = project;
        this.repository = repository;
        this.agents = agents;
        this.suites = suites;
        this.endpoints = endpoints;
        this.validation = validation;
        this.createSystemPrompt = createSystemPrompt;
        this.createModelSwitch = createModelSwitch;
        this.createToolUpdate = createToolUpdate;
        this.mapper = mapper;
        this.license = license;
        this.audit = audit;
    }

    [McpServerTool(Name = "list_theories")]
    [Description("List optimization theories in the current project, with each one's status, rationale and " +
                 "A/B validation outcome. Optionally filter by status.")]
    public async Task<IReadOnlyList<TheoryDto>> ListTheories(
        [Description("Optional status filter: Proposed, Validating, Validated or Invalidated.")] TheoryStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        EnsureFeature();
        var p = await project.GetProjectAsync(cancellationToken);
        IReadOnlyList<IOptimizationTheory> theories = await repository.GetByProjectAsync(p.Id, cancellationToken);
        if (status.HasValue)
            theories = theories.Where(t => t.Status == status.Value).ToList();
        return theories.Select(mapper.ToDto).ToArray();
    }

    [McpServerTool(Name = "get_theory")]
    [Description("Get a single optimization theory by id. It must belong to the current project.")]
    public async Task<TheoryDto> GetTheory(
        [Description("The theory id (GUID), from list_theories.")] Guid theoryId,
        CancellationToken cancellationToken)
    {
        EnsureFeature();
        var p = await project.GetProjectAsync(cancellationToken);
        var theory = await repository.FindAsync(theoryId, cancellationToken);
        if (theory is null || theory.Agent.Project.Id != p.Id)
            throw new McpException($"Theory '{theoryId}' was not found in this project.");
        return mapper.ToDto(theory);
    }

    [McpServerTool(Name = "submit_theory")]
    [Description("Submit ONE optimization theory for an agent: a concrete, evidence-backed change validated " +
                 "by a background baseline-vs-candidate A/B run against a suite. On a win it becomes a reviewable " +
                 "proposal; otherwise it's invalidated. Poll get_theory for the outcome. Choose exactly one " +
                 "`kind` and fill its field. Agent and suite must belong to the current project.")]
    public async Task<TheoryDto> SubmitTheory(
        [Description("The agent id (GUID) to optimize, from list_agents.")] Guid agentId,
        [Description("The suite id (GUID) to validate against, from list_suites.")] Guid suiteId,
        [Description("The change kind: SystemPrompt, ModelSwitch or ToolUpdate.")] McpTheoryChangeKind kind,
        [Description("One-sentence rationale grounded in evidence (e.g. failing cases, traces).")] string rationale,
        [Description("SystemPrompt only: the full rewritten system message.")] string? proposedSystemMessage = null,
        [Description("ModelSwitch only: the proposed model endpoint id (GUID).")] Guid? proposedEndpointId = null,
        [Description("ToolUpdate only: JSON array of tools, each {name, description, parametersJson}.")] string? proposedToolsJson = null,
        [Description("Priority: Low, Medium, High or Critical (default Medium).")] Priority priority = Priority.Medium,
        CancellationToken cancellationToken = default)
    {
        project.RequireWriteScope();
        EnsureFeature();
        if (string.IsNullOrWhiteSpace(rationale))
            throw new McpException("A rationale is required.");

        var p = await project.GetProjectAsync(cancellationToken);
        var agent = await agents.FindAsync(agentId, cancellationToken);
        if (agent is null || agent.Project.Id != p.Id)
            throw new McpException($"Agent '{agentId}' was not found in this project.");
        var suite = await suites.FindAsync(suiteId, cancellationToken);
        if (suite is null || suite.Agent.Project.Id != p.Id)
            throw new McpException($"Suite '{suiteId}' was not found in this project.");

        IOptimizationTheory theory = kind switch
        {
            McpTheoryChangeKind.SystemPrompt => createSystemPrompt(
                agent, suite, TheorySource.External, priority, rationale,
                RequireSystemMessage(proposedSystemMessage), evidenceTestRunIds: []),
            McpTheoryChangeKind.ModelSwitch => createModelSwitch(
                agent, suite, TheorySource.External, priority, rationale,
                await RequireEndpointAsync(proposedEndpointId, cancellationToken), evidenceTestRunIds: []),
            McpTheoryChangeKind.ToolUpdate => createToolUpdate(
                agent, suite, TheorySource.External, priority, rationale,
                ParseTools(proposedToolsJson), evidenceTestRunIds: []),
            _ => throw new McpException("Unsupported change kind."),
        };

        var result = await validation.SubmitAsync(theory, cancellationToken);
        if (result is { Outcome: TheorySubmissionOutcome.Accepted, Theory: { } acceptedTheory })
        {
            audit.LogAudit(
                AuditAction.TheorySubmitted, nameof(IOptimizationTheory), acceptedTheory.Id, agent.Name,
                projectId: p.Id,
                details: JsonSerializer.Serialize(new { source = TheorySource.External.ToString(), kind = kind.ToString() }));
        }

        return (result.Outcome, result.Theory) switch
        {
            (TheorySubmissionOutcome.Accepted, { } accepted) => mapper.ToDto(accepted),
            (TheorySubmissionOutcome.Duplicate, _) => throw new McpException("An identical theory or proposal already exists."),
            (TheorySubmissionOutcome.QuotaExceeded, _) => throw new McpException("Too many theories are awaiting validation in this project. Try again later."),
            _ => throw new McpException("Theory submission failed."),
        };
    }

    private static string RequireSystemMessage(string? proposedSystemMessage)
        => string.IsNullOrWhiteSpace(proposedSystemMessage)
            ? throw new McpException("A SystemPrompt theory requires proposedSystemMessage.")
            : proposedSystemMessage;

    private async Task<IModelEndpoint> RequireEndpointAsync(Guid? endpointId, CancellationToken cancellationToken)
    {
        if (endpointId is null)
            throw new McpException("A ModelSwitch theory requires proposedEndpointId.");
        return await endpoints.FindAsync(endpointId.Value, cancellationToken)
            ?? throw new McpException($"Model endpoint '{endpointId}' was not found.");
    }

    private sealed record ToolSpecDto(string Name, string? Description, string? ParametersJson);

    private static IReadOnlyList<ToolSpecification> ParseTools(string? proposedToolsJson)
    {
        if (string.IsNullOrWhiteSpace(proposedToolsJson))
            throw new McpException("A ToolUpdate theory requires proposedToolsJson (a JSON array of tools).");

        ToolSpecDto[]? specs;
        try
        {
            specs = JsonSerializer.Deserialize<ToolSpecDto[]>(proposedToolsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new McpException($"proposedToolsJson is not valid JSON: {ex.Message}");
        }

        if (specs is null || specs.Length == 0)
            throw new McpException("proposedToolsJson must be a non-empty JSON array of tools.");

        return specs
            .Select(s => new ToolSpecification(
                s.Name,
                s.Description ?? string.Empty,
                string.IsNullOrWhiteSpace(s.ParametersJson) ? ToolArguments.None : ToolArguments.FromJsonSchema(s.ParametersJson)))
            .ToArray();
    }

    private void EnsureFeature()
    {
        if (!license.IsFeatureEnabled(LicenseFeature.OptimizationProposals))
            throw new McpException("Optimization theories are not available on the current license tier.");
    }
}
