using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Message;
using Proxytrace.Storage.Internal.Entities.Inference;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

[StoredDomainEntity(typeof(IAgentCall))]
internal record AgentCallEntity : Entity
{
    public required Guid AgentVersionId { get; init; }
    public required Guid EndpointId { get; init; }
    public required Conversation Request { get; init; }
    public required AssistantMessage? Response { get; init; }
    public required ulong? InputTokens { get; init; }
    public required ulong? OutputTokens { get; init; }
    public required ulong? CachedInputTokens { get; init; }
    public required double? LatencyMs { get; init; }
    public required int HttpStatus { get; init; }
    public required string? FinishReason { get; init; }
    public required string? ErrorMessage { get; init; }
    public required ModelParametersData ModelParameters { get; init; }
    public required Guid? ConversationId { get; init; }
    public required Guid? SessionId { get; init; }

    // Outlier characteristics flagged at ingestion (bitmask). 0 = not an outlier. Persisted as a
    // single byte; a partial index (see AgentCallConfig) serves the "outliers only" trace filter.
    public OutlierFlags OutlierFlags { get; init; }

    // Denormalised summaries populated at write time so the traces-list query can project scalar
    // columns only, without reading/deserialising the Request and Response payload columns.
    public string? RequestPreview { get; init; }
    public int ResponseToolRequestCount { get; init; }

    // Denormalised at write time so token/cache sorts and range filters hit plain indexed columns
    // instead of per-row expressions. Null when the call reported no usage.
    public ulong? TotalTokens { get; init; }
    public double? CacheHitRate { get; init; }

    // One row per distinct tool name requested in the response — backs the ToolName filter's EXISTS
    // semi-join and the project-scoped tool-name picker. See AgentCallToolEntity/AgentCallToolConfig.
    public List<AgentCallToolEntity> Tools { get; init; } = [];
}
