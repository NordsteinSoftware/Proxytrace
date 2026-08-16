namespace Proxytrace.Application.Playground.Internal;

/// <summary>
/// Request payload for playground complete operations.
/// </summary>
public sealed record PlaygroundCompleteRequest(
    Guid AgentId,
    Guid EndpointId,
    string SystemPrompt,
    PlaygroundModelParameters Parameters,
    IReadOnlyList<PlaygroundToolSpecification> Tools,
    IReadOnlyList<PlaygroundMessage> Messages);

/// <summary>
/// Represents a playground model parameters.
/// </summary>
public sealed record PlaygroundModelParameters(
    double? Temperature,
    double? TopP,
    string? ReasoningEffort,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    long? Seed,
    IReadOnlyList<string>? Stop);

/// <summary>
/// Represents a playground tool specification.
/// </summary>
public sealed record PlaygroundToolSpecification(
    string Name,
    string Description,
    IReadOnlyList<PlaygroundToolArgument> Arguments);

/// <summary>
/// Represents a playground tool argument.
/// </summary>
public sealed record PlaygroundToolArgument(
    string Name,
    string? Description,
    string Type,
    bool IsRequired);

/// <summary>
/// Represents a playground message.
/// </summary>
public sealed record PlaygroundMessage(
    string Role,
    string Content,
    IReadOnlyList<PlaygroundToolRequest> ToolRequests,
    string? ToolCallId,
    bool ToolSucceeded,
    string? ToolError);

/// <summary>
/// Request payload for playground tool operations.
/// </summary>
public sealed record PlaygroundToolRequest(string Id, string Name, string Arguments);
