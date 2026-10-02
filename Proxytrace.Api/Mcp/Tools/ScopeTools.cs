using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Proxytrace.Api.Dto.Scopes;
using Proxytrace.Domain.Scope;

namespace Proxytrace.Api.Mcp.Tools;

/// <summary>
/// MCP tools for the current project's scopes — use-case groups of agents, named by clients via the
/// <c>/{project}/{scope}/openai/v1</c> path segment or the <c>x-proxytrace-scope</c> header.
/// </summary>
[McpServerToolType]
internal sealed class ScopeTools
{
    private readonly IMcpProjectAccessor project;
    private readonly IScopeRepository scopes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeTools"/> class.
    /// </summary>
    public ScopeTools(IMcpProjectAccessor project, IScopeRepository scopes)
    {
        this.project = project;
        this.scopes = scopes;
    }

    [McpServerTool(Name = "list_scopes")]
    [Description("List the current project's scopes (use-case groups of agents), most recently active first: " +
                 "id, key, display name, trace/token counts within retention and member agent ids. Pass a scope's " +
                 "id or key to list_traces / get_dashboard to narrow them to that scope.")]
    /// <summary>
    /// Lists the scopes.
    /// </summary>
    public async Task<IReadOnlyList<ScopeListItemDto>> ListScopes(CancellationToken cancellationToken = default)
    {
        var p = await project.GetProjectAsync(cancellationToken);
        var overviews = await scopes.GetOverviewsAsync(p.Id, cancellationToken);
        return overviews.Select(ScopeListItemDto.From).ToArray();
    }
}

/// <summary>
/// Resolves the <c>scope</c> argument the MCP tools accept — a scope id or its key — to an id in the
/// current project. A key is canonicalised exactly as ingestion does, so <c>"Support Agents"</c>
/// finds <c>support-agents</c>, and looked up by key exactly as ingestion admits it — never by
/// re-deriving the id, which would miss a scope stored under any other id.
/// </summary>
internal static class McpScopeArgument
{
    /// <summary>
    /// Returns the scope's id, or throws <see cref="McpException"/> when no such scope exists in
    /// <paramref name="projectId"/> (a scope of another project is reported the same way).
    /// </summary>
    public static async Task<Guid?> ResolveAsync(
        IScopeRepository scopes, Guid projectId, string? scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return null;

        Guid? found;
        if (Guid.TryParse(scope, out var id))
        {
            var byId = await scopes.FindAsync(id, cancellationToken);
            found = byId?.ProjectId == projectId ? byId.Id : null;
        }
        else
        {
            found = ScopeKey.Normalize(scope) is { } key
                ? await scopes.FindIdByKeyAsync(projectId, key, cancellationToken)
                : null;
        }

        return found
               ?? throw new McpException($"Scope '{scope}' was not found in this project. Use list_scopes to see the available scopes.");
    }
}
