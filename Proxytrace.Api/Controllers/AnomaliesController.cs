using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Anomalies;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.Paging;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// Read API for the anomaly dashboard. An anomaly is an agent call whose <see cref="OutlierFlags"/>
/// bitmask is non-zero — flagged statistically at ingestion or by a custom detector — so the recent
/// list is the traces list query with the outlier-only filter (served by the partial outlier index),
/// not a separate query path. The bucketed timeline lives on <c>StatisticsController</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/anomalies")]
public class AnomaliesController : ControllerBase
{
    private readonly IAgentCallRepository repository;
    private readonly IAgentRepository agentRepository;
    private readonly AgentCallDtoMapper agentCallDtoMapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly ICustomAnomalyResultRepository customAnomalyResults;
    private readonly ICustomAnomalyDetectorRepository customAnomalyDetectors;

    public AnomaliesController(
        IAgentCallRepository repository,
        IAgentRepository agentRepository,
        AgentCallDtoMapper agentCallDtoMapper,
        IProjectAccessGuard accessGuard,
        ICustomAnomalyResultRepository customAnomalyResults,
        ICustomAnomalyDetectorRepository customAnomalyDetectors)
    {
        this.repository = repository;
        this.agentRepository = agentRepository;
        this.agentCallDtoMapper = agentCallDtoMapper;
        this.accessGuard = accessGuard;
        this.customAnomalyResults = customAnomalyResults;
        this.customAnomalyDetectors = customAnomalyDetectors;
    }

    // Same scoping rule as AgentCallsController.GetAll (#193): admins (accessible == null) may run
    // any query; non-admins must scope to a project they belong to — directly via projectId or via
    // the agent's project — otherwise the list returns nothing rather than leaking other tenants' rows.
    // Mirrors AgentCallsController.ListScopeAsync — anomalies are traces, filtered the same way.
    private async Task<IReadOnlyCollection<Guid>?> ListScopeAsync(
        Guid? projectId,
        Guid? agentId,
        CancellationToken cancellationToken)
    {
        var scope = await accessGuard.ResolveListScopeAsync(projectId, cancellationToken);
        if (scope is null || scope.IsEmpty() || agentId is not { } aid)
            return scope;

        var agent = await agentRepository.FindAsync(aid, cancellationToken);
        return agent is not null && scope.Contains(agent.Project.Id) ? scope : [];
    }

    /// <summary>
    /// Most recent flagged calls, newest first — the traces list-item shape enriched with the
    /// custom-detector attributions (detector, matched trigger, reasoning) for each call.
    /// </summary>
    [HttpGet("recent")]
    public async Task<PagedResult<AnomalyListItemDto>> GetRecent(
        [FromQuery] Guid? projectId = null,
        [FromQuery] Guid? agentId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var scope = await ListScopeAsync(projectId, agentId, cancellationToken);
        if (scope.IsEmpty())
            return new PagedResult<AnomalyListItemDto>([], 0, page, pageSize);
        var (scopedProjectId, scopedProjectIds) = scope.ToFilterScope();
        var filter = new AgentCallFilter(AgentId: agentId, ProjectId: scopedProjectId, OutlierOnly: true, ProjectIds: scopedProjectIds);
        var (items, total) = await repository.GetFilteredListAsync(filter, page, pageSize, cancellationToken);
        var flaggedIds = items
            .Where(i => ((OutlierFlags)i.OutlierFlags).HasFlag(OutlierFlags.CustomAnomaly))
            .Select(i => i.Id)
            .ToList();
        var hitsByCall = await GetCustomAnomalyHitsAsync(flaggedIds, cancellationToken);
        var dtos = items
            .Select(item => new AnomalyListItemDto(
                agentCallDtoMapper.ToListItemDto(item),
                hitsByCall.GetValueOrDefault(item.Id, [])))
            .ToList();
        return new PagedResult<AnomalyListItemDto>(dtos, total, page, pageSize);
    }

    /// <summary>
    /// Custom-detector attributions for one flagged call — the detector rows (name, matched
    /// trigger, reasoning) the trace detail drawer shows beneath the outlier chips. Empty for a
    /// purely statistical outlier. Other tenants' calls are hidden behind a 404, mirroring
    /// <c>AgentCallsController.Get</c>.
    /// </summary>
    [HttpGet("{callId:guid}")]
    public async Task<ActionResult<IReadOnlyList<CustomAnomalyHitDto>>> GetForCall(
        Guid callId, CancellationToken cancellationToken)
    {
        var call = await repository.FindAsync(callId, cancellationToken);
        if (call is null)
            return NotFound();
        if (!await accessGuard.CanAccessProjectAsync(call.Agent.Project.Id, cancellationToken))
            return NotFound();
        if (!call.OutlierFlags.HasFlag(OutlierFlags.CustomAnomaly))
            return new List<CustomAnomalyHitDto>();
        var hitsByCall = await GetCustomAnomalyHitsAsync([callId], cancellationToken);
        return hitsByCall.GetValueOrDefault(callId, []).ToList();
    }

    // One batch query for the page's attributions, then detector names resolved per distinct id
    // (a handful at most). Results cascade away with their detector, so a missing detector is a
    // delete race mid-request — those hits are dropped rather than shown nameless.
    private async Task<Dictionary<Guid, IReadOnlyList<CustomAnomalyHitDto>>> GetCustomAnomalyHitsAsync(
        IReadOnlyList<Guid> flaggedIds,
        CancellationToken cancellationToken)
    {
        if (flaggedIds.Count == 0)
            return [];

        var results = await customAnomalyResults.GetByAgentCallIdsAsync(flaggedIds, cancellationToken);
        var detectorNames = new Dictionary<Guid, string>();
        foreach (var detectorId in results.Select(r => r.DetectorId).Distinct())
        {
            var detector = await customAnomalyDetectors.FindAsync(detectorId, cancellationToken);
            if (detector is not null)
                detectorNames[detectorId] = detector.Name;
        }

        return results
            .Where(r => detectorNames.ContainsKey(r.DetectorId))
            .GroupBy(r => r.AgentCallId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CustomAnomalyHitDto>)g
                    .Select(r => new CustomAnomalyHitDto(
                        r.DetectorId, detectorNames[r.DetectorId], r.MatchedTrigger, r.Reasoning))
                    .ToList());
    }
}
