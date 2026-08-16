using Proxytrace.Api.Dto.Inference;

namespace Proxytrace.Api.Dto.Agents;

/// <summary>
/// Data transfer object representing a agent.
/// </summary>
public record AgentDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    string SystemMessage,
    IReadOnlyList<ToolSpecificationDto> Tools,
    Guid EndpointId,
    string EndpointName,
    ModelParametersDto ModelParameters,
    bool IsSystemAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Lightweight agent projection for lists (agents grid, dashboard agents, traces filter bar). Keeps
/// the row fields plus a tool count, but drops the fat <see cref="AgentDto"/>'s system message, full
/// tool specs and model parameters. The full agent is fetched per-selection via
/// <c>GET /api/agents/{id}</c>.
/// </summary>
public record AgentListItemDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Name,
    int ToolCount,
    Guid EndpointId,
    string EndpointName,
    bool IsSystemAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>
/// Request payload for update agent endpoint operations.
/// </summary>
public record UpdateAgentEndpointRequest(Guid EndpointId);

/// <summary>
/// Request payload for move version operations.
/// </summary>
public record MoveVersionRequest(Guid TargetAgentId);

/// <summary>
/// Data transfer object representing a agent version.
/// </summary>
public record AgentVersionDto(
    Guid Id,
    Guid AgentId,
    int VersionNumber,
    string SystemMessage,
    IReadOnlyList<ToolSpecificationDto> Tools,
    string Fingerprint,
    DateTimeOffset CreatedAt);

/// <summary>
/// Data transfer object representing a tool specification.
/// </summary>
public record ToolSpecificationDto(
    string Name,
    string Description,
    IReadOnlyList<ToolArgumentDto> Arguments);

/// <summary>
/// Data transfer object representing a tool argument.
/// </summary>
public record ToolArgumentDto(
    string Name,
    string? Description,
    string Type,
    bool IsRequired,
    IReadOnlyList<string>? EnumValues);
