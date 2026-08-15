namespace Proxytrace.Api.Dto.Playground;

/// <summary>
/// Data transfer object representing a playground complete request.
/// </summary>
public sealed record PlaygroundCompleteRequestDto(
    Guid AgentId,
    Guid EndpointId,
    string SystemPrompt,
    PlaygroundModelParametersDto Parameters,
    IReadOnlyList<PlaygroundToolSpecificationDto> Tools,
    IReadOnlyList<PlaygroundMessageDto> Messages);

/// <summary>
/// Data transfer object representing a playground model parameters.
/// </summary>
public sealed record PlaygroundModelParametersDto(
    double? Temperature,
    double? TopP,
    string? ReasoningEffort,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    long? Seed,
    IReadOnlyList<string>? Stop);

/// <summary>
/// Data transfer object representing a playground tool specification.
/// </summary>
public sealed record PlaygroundToolSpecificationDto(
    string Name,
    string Description,
    IReadOnlyList<PlaygroundToolArgumentDto> Arguments);

/// <summary>
/// Data transfer object representing a playground tool argument.
/// </summary>
public sealed record PlaygroundToolArgumentDto(
    string Name,
    string? Description,
    string Type,
    bool IsRequired);

/// <summary>
/// Data transfer object representing a playground message.
/// </summary>
public sealed record PlaygroundMessageDto(
    string Role,
    string Content,
    IReadOnlyList<PlaygroundToolRequestDto> ToolRequests,
    string? ToolCallId,
    bool ToolSucceeded,
    string? ToolError);

/// <summary>
/// Data transfer object representing a playground tool request.
/// </summary>
public sealed record PlaygroundToolRequestDto(string Id, string Name, string Arguments);
