using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Inference;

namespace Proxytrace.Api.Dto.AgentCalls;

public record AgentCallDto(
    Guid Id,
    Guid? AgentId,
    string? AgentName,
    string Model,
    string Provider,
    IReadOnlyList<AgentCallMessageDto> Request,
    AgentCallMessageDto? Response,
    IReadOnlyList<ToolSpecificationDto> Tools,
    ulong? InputTokens,
    ulong? OutputTokens,
    ulong? CachedInputTokens,
    double? DurationMs,
    int HttpStatus,
    string? FinishReason,
    string? ErrorMessage,
    decimal? CostEur,
    ModelParametersDto ModelParameters,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ConversationId,
    Guid? SessionId,
    int OutlierFlags);

public record AgentCallMessageDto(string Role, string Content, IReadOnlyList<AgentCallToolRequestDto> ToolRequests, string? ToolCallId = null);

public record AgentCallToolRequestDto(string Id, string Name, string Arguments);

/// <summary>
/// Lightweight agent-call projection for the traces table / dashboard live stream. Carries only the
/// row fields plus two precomputed summaries — the first-user-message preview and the response
/// tool-request count — so the list never ships the fat <see cref="AgentCallDto"/> (full request,
/// response, tool specs and model parameters). The full DTO is fetched per-selection via
/// <c>GET /api/agent-calls/{id}</c>.
/// </summary>
public record AgentCallListItemDto(
    Guid Id,
    Guid? AgentId,
    string? AgentName,
    string Model,
    string Provider,
    string? MessagePreview,
    int ToolCount,
    ulong? InputTokens,
    ulong? OutputTokens,
    ulong? CachedInputTokens,
    double? DurationMs,
    int HttpStatus,
    string? FinishReason,
    string? ErrorMessage,
    decimal? CostEur,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ConversationId,
    Guid? SessionId,
    int OutlierFlags);
