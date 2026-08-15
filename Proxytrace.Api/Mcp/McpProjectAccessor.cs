using ModelContextProtocol;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Project;

namespace Proxytrace.Api.Mcp;

internal sealed class McpProjectAccessor : IMcpProjectAccessor
{
    /// <summary>
    /// Key under which the MCP authentication handler stashes the resolved project id on
    /// <see cref="HttpContext.Items"/>. The analogue of <c>CurrentUserAccessor.UserIdItemKey</c>.
    /// </summary>
    internal const string ProjectIdItemKey = "Proxytrace.Mcp.ProjectId";

    /// <summary>
    /// Key under which the MCP authentication handler stashes the granted <see cref="ApiKeyScopes"/>.
    /// </summary>
    internal const string ScopesItemKey = "Proxytrace.Mcp.Scopes";

    private const string ProjectItemKey = "Proxytrace.Mcp.Project";

    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IProjectRepository projects;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpProjectAccessor"/> class.
    /// </summary>
    public McpProjectAccessor(IHttpContextAccessor httpContextAccessor, IProjectRepository projects)
    {
        this.httpContextAccessor = httpContextAccessor;
        this.projects = projects;
    }

    /// <summary>
    /// Gets the project asynchronously.
    /// </summary>
    public async Task<IProject> GetProjectAsync(CancellationToken cancellationToken = default)
    {
        var ctx = httpContextAccessor.HttpContext
            ?? throw new McpException("No active request context for the MCP call.");

        if (ctx.Items[ProjectItemKey] is IProject cached)
        {
            return cached;
        }

        if (ctx.Items[ProjectIdItemKey] is not Guid projectId)
        {
            throw new McpException("The MCP API key is not associated with a project.");
        }

        var project = await projects.FindAsync(projectId, cancellationToken)
            ?? throw new McpException("The project for this MCP API key no longer exists.");

        ctx.Items[ProjectItemKey] = project;
        return project;
    }

    /// <summary>
    /// Require write scope.
    /// </summary>
    public void RequireWriteScope()
    {
        var ctx = httpContextAccessor.HttpContext
            ?? throw new McpException("No active request context for the MCP call.");

        if (ctx.Items[ScopesItemKey] is not ApiKeyScopes scopes || !scopes.HasFlag(ApiKeyScopes.McpWrite))
        {
            throw new McpException(
                "This API key is read-only. Mutating tools require an API key with the MCP write scope.");
        }
    }
}
