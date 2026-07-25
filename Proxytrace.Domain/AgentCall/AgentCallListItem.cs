namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Lightweight read model for the traces list. Carries only the scalar row fields plus the two
/// precomputed summaries the list renders — the first-user-message preview and the response
/// tool-request count — so the list query never reads or deserialises the fat request/response/
/// model-parameter payload columns. The full <see cref="IAgentCall"/> is loaded per-selection.
/// </summary>
public sealed record AgentCallListItem(
    Guid Id,
    Guid AgentId,
    string AgentName,
    string ModelName,
    string ProviderName,
    string? MessagePreview,
    int ToolCount,
    ulong? InputTokens,
    ulong? OutputTokens,
    ulong? CachedInputTokens,
    double? LatencyMs,
    int HttpStatus,
    string? FinishReason,
    string? ErrorMessage,
    decimal? Cost,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? ConversationId,
    Guid? SessionId,
    OutlierFlags OutlierFlags);
