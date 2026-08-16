using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Dto.Anomalies;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Proxytrace.Licensing;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// CRUD for user-defined LLM-based anomaly detectors. Each detector owns a hidden system agent —
/// its system prompt holds the review instructions, its endpoint selects the judge model —
/// provisioned/updated/removed alongside the detector (mirrors the agentic-evaluator flow).
/// </summary>
[ApiController]
[Authorize]
[Route("api/anomaly-detectors")]
[RequiresFeature(LicenseFeature.CustomAnomalyDetectors)]
public class CustomAnomalyDetectorsController : ControllerBase
{
    private readonly ICustomAnomalyDetectorRepository detectorRepository;
    private readonly IProjectRepository projectRepository;
    private readonly IAgentRepository agentRepository;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly IAgent.CreateNew createAgent;
    private readonly ICustomAnomalyDetector.CreateNew createDetector;
    private readonly IPromptTemplate.Create createPrompt;
    private readonly IModelParameters.Create createParameters;
    private readonly ITransaction transaction;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyDetectorsController"/> class.
    /// </summary>
    public CustomAnomalyDetectorsController(
        ICustomAnomalyDetectorRepository detectorRepository,
        IProjectRepository projectRepository,
        IAgentRepository agentRepository,
        IRepository<IModelEndpoint> endpoints,
        IAgent.CreateNew createAgent,
        ICustomAnomalyDetector.CreateNew createDetector,
        IPromptTemplate.Create createPrompt,
        IModelParameters.Create createParameters,
        ITransaction transaction,
        IProjectAccessGuard accessGuard,
        ILogger<Audit> audit)
    {
        this.detectorRepository = detectorRepository;
        this.projectRepository = projectRepository;
        this.agentRepository = agentRepository;
        this.endpoints = endpoints;
        this.createAgent = createAgent;
        this.createDetector = createDetector;
        this.createPrompt = createPrompt;
        this.createParameters = createParameters;
        this.transaction = transaction;
        this.accessGuard = accessGuard;
        this.audit = audit;
    }

    /// <summary>
    /// Returns all custom anomaly detectors for the given project. Returns an empty list (not 404)
    /// when the caller cannot access the project. Requires the <c>CustomAnomalyDetectors</c> license
    /// feature.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<CustomAnomalyDetectorDto>> GetAll(
        [FromQuery] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        // Empty rather than 404: a non-member must not learn whether the project exists.
        if (!await accessGuard.CanAccessProjectAsync(projectId, cancellationToken))
            return [];

        var detectors = await detectorRepository.GetByProjectAsync(projectId, cancellationToken);
        return detectors.Select(ToDto).ToArray();
    }

    /// <summary>
    /// Returns a single custom anomaly detector by id. Returns 404 when it does not exist or the
    /// caller cannot access its project.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomAnomalyDetectorDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var detector = await detectorRepository.FindAsync(id, cancellationToken);
        if (detector is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(detector.Project.Id, cancellationToken))
            return NotFound();
        return ToDto(detector);
    }

    /// <summary>
    /// Creates a new LLM-based anomaly detector and its hidden system agent. Each detector carries
    /// a set of regex/literal triggers, a review-instructions system prompt, a judge model endpoint,
    /// and an optional per-agent scope. Returns 400 when the endpoint or any agent id is not found,
    /// or when the trigger patterns are invalid.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CustomAnomalyDetectorDto>> Create(
        [FromBody] CreateCustomAnomalyDetectorRequest request,
        CancellationToken cancellationToken)
    {
        // Emit the audit only AFTER the transaction commits — LogAudit is a fire-and-forget write
        // to a decoupled channel, so emitting inside the lambda would leave a phantom audit row if
        // the commit then fails (mirrors EvaluatorsController).
        (Guid Id, string Name, Guid ProjectId, string Details)? created = null;
        var result = await transaction.InvokeAsync<ActionResult<CustomAnomalyDetectorDto>>(async () =>
        {
            var project = await projectRepository.FindAsync(request.ProjectId, cancellationToken);
            if (project is null)
                return BadRequest($"Project {request.ProjectId} not found.");
            if (!await accessGuard.CanAccessProjectAsync(project.Id, cancellationToken))
                return NotFound();

            if (ValidateRequest(request.Name, request.Instructions, request.Triggers, request.AllAgents, request.AgentIds) is { } error)
                return BadRequest(error);

            var endpoint = await endpoints.FindAsync(request.EndpointId, cancellationToken);
            if (endpoint is null)
                return BadRequest($"Endpoint {request.EndpointId} not found.");

            var (scopedAgents, agentError) = await ResolveScopedAgentsAsync(
                request.AllAgents, request.AgentIds, project.Id, cancellationToken);
            if (agentError is not null)
                return BadRequest(agentError);

            // The hidden system agent carries the review instructions as its system prompt;
            // temperature 0 for a deterministic judge (mirrors the evaluator provisioner).
            var agent = await createAgent(
                name: request.Name,
                systemPrompt: createPrompt(request.Name, request.Instructions),
                tools: [],
                endpoint: endpoint,
                project: project,
                modelParameters: createParameters(temperature: 0.0),
                isSystemAgent: true).AddAsync(cancellationToken);

            var detector = createDetector(
                request.Name, agent, ToTriggers(request.Triggers), request.AllAgents, scopedAgents,
                request.IsEnabled, request.BlockUpstream);
            var saved = await detectorRepository.AddAsync(detector, cancellationToken);

            created = (saved.Id, saved.Name, project.Id, BuildAuditDetails(saved));
            return CreatedAtAction(nameof(Get), new { id = saved.Id }, ToDto(saved));
        });

        if (created is { } c)
            audit.LogAudit(
                AuditAction.CustomAnomalyDetectorCreated, nameof(ICustomAnomalyDetector), c.Id, c.Name,
                projectId: c.ProjectId, details: c.Details);
        return result;
    }

    /// <summary>
    /// Replaces all mutable fields of a custom anomaly detector. If the endpoint changes, the hidden
    /// agent is updated; new instructions create a new prompt version on the hidden agent. Returns
    /// 404 when the detector does not exist or the caller cannot access its project.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomAnomalyDetectorDto>> Update(
        Guid id,
        [FromBody] UpdateCustomAnomalyDetectorRequest request,
        CancellationToken cancellationToken)
    {
        (Guid Id, string Name, Guid ProjectId, string Details)? updated = null;
        var result = await transaction.InvokeAsync<ActionResult<CustomAnomalyDetectorDto>>(async () =>
        {
            var detector = await detectorRepository.FindAsync(id, cancellationToken);
            if (detector is null)
                return NotFound();
            if (!await accessGuard.CanAccessProjectAsync(detector.Project.Id, cancellationToken))
                return NotFound();

            if (ValidateRequest(request.Name, request.Instructions, request.Triggers, request.AllAgents, request.AgentIds) is { } error)
                return BadRequest(error);

            var (scopedAgents, agentError) = await ResolveScopedAgentsAsync(
                request.AllAgents, request.AgentIds, detector.Project.Id, cancellationToken);
            if (agentError is not null)
                return BadRequest(agentError);

            var agent = detector.Agent;
            if (request.EndpointId is { } endpointId && endpointId != agent.Endpoint.Id)
            {
                var endpoint = await endpoints.FindAsync(endpointId, cancellationToken);
                if (endpoint is null)
                    return BadRequest($"Endpoint {endpointId} not found.");
                agent = await agent.ChangeEndpoint(endpoint, cancellationToken);
            }

            // New instructions become a new version of the hidden agent's system prompt (mirrors
            // the agentic-evaluator update flow).
            agent = await agent.CreateNewVersionAsync(
                createPrompt(request.Name, request.Instructions), agent.Tools, cancellationToken);

            var saved = await detector.Update(
                request.Name, ToTriggers(request.Triggers), request.AllAgents, scopedAgents, request.IsEnabled,
                request.BlockUpstream, cancellationToken);

            updated = (saved.Id, saved.Name, saved.Project.Id, BuildAuditDetails(saved));
            return ToDto(saved, agent);
        });

        if (updated is { } u)
            audit.LogAudit(
                AuditAction.CustomAnomalyDetectorUpdated, nameof(ICustomAnomalyDetector), u.Id, u.Name,
                projectId: u.ProjectId, details: u.Details);
        return result;
    }

    /// <summary>
    /// Deletes a custom anomaly detector and its hidden system agent in a single transaction.
    /// Returns 404 when the detector does not exist or the caller cannot access its project.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var detector = await detectorRepository.FindAsync(id, cancellationToken);
        if (detector is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(detector.Project.Id, cancellationToken))
            return NotFound();

        var projectId = detector.Project.Id;
        var hiddenAgentId = detector.Agent.Id;

        // Remove the detector (its results cascade with it) and its hidden system agent together —
        // the agent is internal plumbing and must not linger once the detector is gone.
        var removed = await transaction.InvokeAsync(async () =>
        {
            if (!await detectorRepository.RemoveAsync(id, cancellationToken))
                return false;
            await agentRepository.RemoveAsync(hiddenAgentId, cancellationToken);
            return true;
        });
        if (!removed)
            return NotFound();

        audit.LogAudit(
            AuditAction.CustomAnomalyDetectorDeleted, nameof(ICustomAnomalyDetector), id, detector.Name,
            projectId: projectId);
        return NoContent();
    }

    private static string? ValidateRequest(
        string name,
        string instructions,
        IReadOnlyList<AnomalyTriggerDto> triggers,
        bool allAgents,
        IReadOnlyList<Guid>? agentIds)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required.";

        if (string.IsNullOrWhiteSpace(instructions))
            return "Instructions are required.";

        if (triggers.Count is 0 or > ICustomAnomalyDetector.MaxTriggers)
            return $"A detector must have between 1 and {ICustomAnomalyDetector.MaxTriggers} triggers.";

        foreach (var trigger in triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Pattern))
                return "A trigger pattern cannot be empty.";

            if (trigger.Kind != TriggerKind.Regex)
                continue;

            try
            {
                // Validate with the SAME options the review pipeline matches with — NonBacktracking
                // rejects backreferences/lookarounds (NotSupportedException) at construction.
                _ = new Regex(trigger.Pattern, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return $"Trigger regex '{trigger.Pattern}' is invalid: {ex.Message}";
            }
        }

        if (!allAgents && (agentIds is null || agentIds.Count == 0))
            return "A detector that does not apply to all agents must select at least one agent.";

        return null;
    }

    private async Task<(IReadOnlyCollection<IAgent> ScopedAgents, string? Error)> ResolveScopedAgentsAsync(
        bool allAgents,
        IReadOnlyList<Guid>? agentIds,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (allAgents || agentIds is null)
            return ([], null);

        var agents = new List<IAgent>(agentIds.Count);
        foreach (var agentId in agentIds.Distinct())
        {
            var agent = await agentRepository.FindAsync(agentId, cancellationToken);
            if (agent is null || agent.Project.Id != projectId)
                return ([], $"Agent {agentId} not found.");
            agents.Add(agent);
        }

        return (agents, null);
    }

    private static IReadOnlyList<AnomalyTrigger> ToTriggers(IReadOnlyList<AnomalyTriggerDto> triggers)
        => triggers.Select(t => new AnomalyTrigger(t.Kind, t.Pattern.Trim())).ToArray();

    private static string BuildAuditDetails(ICustomAnomalyDetector detector)
        => JsonSerializer.Serialize(new
        {
            triggerCount = detector.Triggers.Count,
            allAgents = detector.AllAgents,
            isEnabled = detector.IsEnabled,
            blockUpstream = detector.BlockUpstream,
        });

    private static CustomAnomalyDetectorDto ToDto(ICustomAnomalyDetector detector)
        => ToDto(detector, detector.Agent);

    private static CustomAnomalyDetectorDto ToDto(ICustomAnomalyDetector detector, IAgent agent)
        => new(
            Id: detector.Id,
            Name: detector.Name,
            Instructions: agent.SystemPrompt.Template,
            ProjectId: detector.Project.Id,
            EndpointId: agent.Endpoint.Id,
            EndpointName: agent.Endpoint.Model.Name,
            Triggers: detector.Triggers.Select(t => new AnomalyTriggerDto(t.Kind, t.Pattern)).ToArray(),
            AllAgents: detector.AllAgents,
            AgentIds: detector.ScopedAgents.Select(a => a.Id).ToArray(),
            IsEnabled: detector.IsEnabled,
            BlockUpstream: detector.BlockUpstream,
            CreatedAt: detector.CreatedAt,
            UpdatedAt: detector.UpdatedAt);
}
