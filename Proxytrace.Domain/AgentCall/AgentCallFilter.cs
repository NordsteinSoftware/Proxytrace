namespace Proxytrace.Domain.AgentCall;

public record AgentCallFilter(
    Guid? AgentId = null,
    Guid? ProjectId = null,
    Guid? EndpointId = null,
    string? Model = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int? HttpStatus = null,
    bool IncludeSystemAgents = true,
    string? Query = null,
    Guid? ConversationId = null,
    bool OutlierOnly = false,
    OutlierFlags? AnomalyFlags = null,
    int? HttpStatusClass = null,
    ulong? MinTokens = null,
    ulong? MaxTokens = null,
    double? MinLatencyMs = null,
    double? MaxLatencyMs = null,
    string? ToolName = null,
    AgentCallSortField SortBy = AgentCallSortField.CreatedAt,
    bool SortDescending = true,
    Guid? SessionId = null);
