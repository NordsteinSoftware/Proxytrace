using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// MCP tools for inspecting the agents of the current project (the project the API key belongs to).
/// </summary>
[McpServerToolType]
internal sealed class AgentTools
{
    private readonly IMcpProjectAccessor project;
    private readonly IAgentRepository agents;
    private readonly IAgentCallRepository agentCalls;
    private readonly AgentDtoMapper mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentTools"/> class.
    /// </summary>
    public AgentTools(
        IMcpProjectAccessor project,
        IAgentRepository agents,
        IAgentCallRepository agentCalls,
        AgentDtoMapper mapper)
    {
        this.project = project;
        this.agents = agents;
        this.agentCalls = agentCalls;
        this.mapper = mapper;
    }

    [McpServerTool(Name = "list_agents")]
    [Description("List the agents in the current project (the project the API key belongs to). " +
                 "Returns id, name, model endpoint and tool count for each, most recently used first.")]
    /// <summary>
    /// Lists the agents.
    /// </summary>
    public async Task<IReadOnlyList<AgentListItemDto>> ListAgents(CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var all = await agents.GetByProjectAsync(p.Id, cancellationToken);
        var lastCallTimes = await agentCalls.GetLastCallTimesAsync(cancellationToken);
        return all
            .Where(a => !a.IsArchived)
            .OrderByDescending(a => lastCallTimes.TryGetValue(a.Id, out var t) ? t : DateTimeOffset.MinValue)
            .ThenByDescending(a => a.UpdatedAt)
            .Select(a => mapper.ToListItemDto(a, lastCallTimes.TryGetValue(a.Id, out var t) ? t : null))
            .ToArray();
    }

    [McpServerTool(Name = "get_agent")]
    [Description("Get a single agent by id, including its full system prompt and tool specifications. " +
                 "The agent must belong to the current project.")]
    /// <summary>
    /// Gets the agent.
    /// </summary>
    public async Task<AgentDto> GetAgent(
        [Description("The agent id (GUID), as returned by list_agents.")] Guid agentId,
        CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var agent = await agents.FindAsync(agentId, cancellationToken);
        if (agent is null || agent.Project.Id != p.Id)
        {
            throw new McpException($"Agent '{agentId}' was not found in this project.");
        }

        var lastCallTime = await agentCalls.GetLastCallTimeAsync(agent.Id, cancellationToken);
        return mapper.ToDto(agent, lastCallTime);
    }
}
