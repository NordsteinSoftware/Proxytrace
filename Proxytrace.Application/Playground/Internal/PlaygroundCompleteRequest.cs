namespace Proxytrace.Application.Playground.Internal;

public sealed record PlaygroundCompleteRequest(
    Guid AgentId,
    Guid EndpointId,
    string SystemPrompt,
    PlaygroundModelParameters Parameters,
    IReadOnlyList<PlaygroundToolSpecification> Tools,
    IReadOnlyList<PlaygroundMessage> Messages);

public sealed record PlaygroundModelParameters(
    double? Temperature,
    double? TopP,
    string? ReasoningEffort,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    long? Seed,
    IReadOnlyList<string>? Stop);

public sealed record PlaygroundToolSpecification(
    string Name,
    string Description,
    IReadOnlyList<PlaygroundToolArgument> Arguments);

public sealed record PlaygroundToolArgument(
    string Name,
    string? Description,
    string Type,
    bool IsRequired);

public sealed record PlaygroundMessage(
    string Role,
    string Content,
    IReadOnlyList<PlaygroundToolRequest> ToolRequests,
    string? ToolCallId,
    bool ToolSucceeded,
    string? ToolError);

public sealed record PlaygroundToolRequest(string Id, string Name, string Arguments);
