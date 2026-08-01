namespace Proxytrace.Api.Dto.Playground;

public sealed record PlaygroundCompleteRequestDto(
    Guid AgentId,
    Guid EndpointId,
    string SystemPrompt,
    PlaygroundModelParametersDto Parameters,
    IReadOnlyList<PlaygroundToolSpecificationDto> Tools,
    IReadOnlyList<PlaygroundMessageDto> Messages);

public sealed record PlaygroundModelParametersDto(
    double? Temperature,
    double? TopP,
    string? ReasoningEffort,
    double? FrequencyPenalty,
    double? PresencePenalty,
    int? MaxTokens,
    long? Seed,
    IReadOnlyList<string>? Stop);

public sealed record PlaygroundToolSpecificationDto(
    string Name,
    string Description,
    IReadOnlyList<PlaygroundToolArgumentDto> Arguments);

public sealed record PlaygroundToolArgumentDto(
    string Name,
    string? Description,
    string Type,
    bool IsRequired);

public sealed record PlaygroundMessageDto(
    string Role,
    string Content,
    IReadOnlyList<PlaygroundToolRequestDto> ToolRequests,
    string? ToolCallId,
    bool ToolSucceeded,
    string? ToolError);

public sealed record PlaygroundToolRequestDto(string Id, string Name, string Arguments);
