using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Messages;
using Proxytrace.Storage.Internal.Entities.Inference;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

[StoredDomainEntity(typeof(IAgentCall))]
internal record AgentCallEntity : Entity
{
    /// <summary>
    /// Gets or sets the agent version id.
    /// </summary>
    public required Guid AgentVersionId { get; init; }
    /// <summary>
    /// Gets or sets the endpoint id.
    /// </summary>
    public required Guid EndpointId { get; init; }
    /// <summary>
    /// Gets or sets the request.
    /// </summary>
    public required Conversation Request { get; init; }
    /// <summary>
    /// Gets or sets the response.
    /// </summary>
    public required AssistantMessage? Response { get; init; }
    /// <summary>
    /// Gets or sets the input tokens.
    /// </summary>
    public required ulong? InputTokens { get; init; }
    /// <summary>
    /// Gets or sets the output tokens.
    /// </summary>
    public required ulong? OutputTokens { get; init; }
    /// <summary>
    /// Gets or sets the cached input tokens.
    /// </summary>
    public required ulong? CachedInputTokens { get; init; }
    /// <summary>
    /// Gets or sets the latency ms.
    /// </summary>
    public required double? LatencyMs { get; init; }
    /// <summary>
    /// Gets or sets the http status.
    /// </summary>
    public required int HttpStatus { get; init; }
    /// <summary>
    /// Gets or sets the finish reason.
    /// </summary>
    public required string? FinishReason { get; init; }
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public required string? ErrorMessage { get; init; }
    /// <summary>
    /// Gets or sets the model parameters.
    /// </summary>
    public required ModelParametersData ModelParameters { get; init; }
    /// <summary>
    /// Gets or sets the conversation id.
    /// </summary>
    public required Guid? ConversationId { get; init; }
    /// <summary>
    /// Gets or sets the session id.
    /// </summary>
    public required Guid? SessionId { get; init; }

    // The inbound Proxytrace API key this call authenticated with, or null when unattributable (the
    // upstream-key auth path, or any row written before key attribution existed). Deliberately
    // FK-free — like SessionId/ConversationId — so revoking a key never cascades away telemetry.
    // Backs the per-key cost breakdown and key-scoped budgets.
    /// <summary>
    /// Gets or sets the api key id.
    /// </summary>
    public Guid? ApiKeyId { get; init; }

    // Outlier characteristics flagged at ingestion (bitmask). 0 = not an outlier. Persisted as a
    // single byte; a partial index (see AgentCallConfig) serves the "outliers only" trace filter.
    /// <summary>
    /// Gets or sets the outlier flags.
    /// </summary>
    public OutlierFlags OutlierFlags { get; init; }

    // Denormalised summaries populated at write time so the traces-list query can project scalar
    // columns only, without reading/deserialising the Request and Response payload columns.
    /// <summary>
    /// Gets or sets the request preview.
    /// </summary>
    public string? RequestPreview { get; init; }
    /// <summary>
    /// Gets or sets the response tool request count.
    /// </summary>
    public int ResponseToolRequestCount { get; init; }

    // Denormalised at write time so token/cache sorts and range filters hit plain indexed columns
    // instead of per-row expressions. Null when the call reported no usage.
    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public ulong? TotalTokens { get; init; }
    /// <summary>
    /// Gets or sets the cache hit rate.
    /// </summary>
    public double? CacheHitRate { get; init; }

    // One row per distinct tool name requested in the response — backs the ToolName filter's EXISTS
    // semi-join and the project-scoped tool-name picker. See AgentCallToolEntity/AgentCallToolConfig.
    /// <summary>
    /// Gets or sets the tools.
    /// </summary>
    public List<AgentCallToolEntity> Tools { get; init; } = [];
}
