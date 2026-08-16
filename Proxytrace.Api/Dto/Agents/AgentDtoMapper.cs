using Proxytrace.Api.Dto.Inference;
using Proxytrace.Api.Dto.Tools;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;

namespace Proxytrace.Api.Dto.Agents;

/// <summary>
/// Maps <see cref="IAgent"/> domain entities to <see cref="AgentDto"/>.
/// Shared by the agents controller and aggregate view endpoints.
/// </summary>
public sealed class AgentDtoMapper
{
    private readonly ToolDtoMapper toolDtoMapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentDtoMapper"/> class.
    /// </summary>
    public AgentDtoMapper(ToolDtoMapper toolDtoMapper)
    {
        this.toolDtoMapper = toolDtoMapper;
    }

    /// <summary>
    /// To dto.
    /// </summary>
    public AgentDto ToDto(IAgent a, DateTimeOffset? lastUsedAt) => new(
        a.Id,
        a.Project.Id,
        a.Project.Name,
        a.Name,
        a.SystemPrompt.Template,
        [.. a.Tools.Select(toolDtoMapper.ToToolSpecDto)],
        a.Endpoint.Id,
        $"{a.Endpoint.Model.Name} / {a.Endpoint.Provider.Name}",
        ModelParametersDto.FromDomain(a.ModelParameters),
        a.IsSystemAgent,
        a.CreatedAt,
        a.UpdatedAt,
        lastUsedAt);

    /// <summary>
    /// Lightweight projection for agent lists — row fields + tool count, skipping the system message,
    /// full tool specs and model parameters. Mirrors <see cref="ToDto(IAgent, DateTimeOffset?)"/>.
    /// </summary>
    public AgentListItemDto ToListItemDto(IAgent a, DateTimeOffset? lastUsedAt) => new(
        a.Id,
        a.Project.Id,
        a.Project.Name,
        a.Name,
        a.Tools.Count,
        a.Endpoint.Id,
        $"{a.Endpoint.Model.Name} / {a.Endpoint.Provider.Name}",
        a.IsSystemAgent,
        a.CreatedAt,
        a.UpdatedAt,
        lastUsedAt);

    /// <summary>
    /// To dto.
    /// </summary>
    public AgentVersionDto ToDto(IAgentVersion v, string fingerprint) => new(
        v.Id,
        v.AgentId,
        v.VersionNumber,
        v.SystemPrompt.Template,
        [.. v.Tools.Select(toolDtoMapper.ToToolSpecDto)],
        fingerprint,
        v.CreatedAt);
}
