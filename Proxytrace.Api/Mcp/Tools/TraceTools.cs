using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Domain.AgentCall;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// MCP tools for searching and reading captured LLM traces (agent calls) in the current project.
/// </summary>
[McpServerToolType]
internal sealed class TraceTools
{
    private readonly IMcpProjectAccessor project;
    private readonly IAgentCallRepository calls;
    private readonly AgentCallDtoMapper mapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="TraceTools"/> class.
    /// </summary>
    public TraceTools(IMcpProjectAccessor project, IAgentCallRepository calls, AgentCallDtoMapper mapper)
    {
        this.project = project;
        this.calls = calls;
        this.mapper = mapper;
    }

    [McpServerTool(Name = "list_traces")]
    [Description("Search captured LLM traces (agent calls) in the current project, newest first. " +
                 "Optionally filter by agent, a free-text query, or HTTP status.")]
    /// <summary>
    /// Lists the traces.
    /// </summary>
    public async Task<IReadOnlyList<AgentCallListItemDto>> ListTraces(
        [Description("Optional agent id (GUID) to restrict the search to a single agent.")] Guid? agentId = null,
        [Description("Optional free-text query matched against the captured request/response.")] string? query = null,
        [Description("Optional HTTP status code to filter by (e.g. 200 or 500).")] int? httpStatus = null,
        [Description("Maximum number of traces to return (1-100, default 25).")] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        limit = Math.Clamp(limit, 1, 100);
        var filter = new AgentCallFilter(agentId, p.Id, null, null, null, null, httpStatus, true, query, null);
        var (items, _) = await calls.GetFilteredListAsync(filter, 1, limit, cancellationToken);
        return items.Select(mapper.ToListItemDto).ToArray();
    }

    [McpServerTool(Name = "get_trace")]
    [Description("Get a single captured trace (agent call) by id, including the full request, response and tools. " +
                 "The trace must belong to the current project.")]
    /// <summary>
    /// Gets the trace.
    /// </summary>
    public async Task<AgentCallDto> GetTrace(
        [Description("The trace id (GUID), as returned by list_traces.")] Guid traceId,
        CancellationToken cancellationToken)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var call = await calls.FindAsync(traceId, cancellationToken);
        if (call is null || call.Agent.Project.Id != p.Id)
        {
            throw new McpException($"Trace '{traceId}' was not found in this project.");
        }

        return mapper.ToDto(call);
    }
}
