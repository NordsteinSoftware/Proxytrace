namespace Proxytrace.Api.Dto.TestRuns;

/// <summary>
/// Data transfer object representing a test case fixture.
/// </summary>
public record TestCaseFixtureDto(
    TestCaseInputDto Input,
    OutputValueDto Expected,
    OutputValueDto Actual,
    EvaluatorFixtureResultDto[] Evaluators,
    RuntimeBreakdownDto Runtime,
    EndpointUsageDto[] Endpoints
);

/// <summary>
/// Data transfer object representing a test case input.
/// </summary>
public record TestCaseInputDto(TestCaseMessageDto[] Messages);

/// <summary>
/// Data transfer object representing a test case message.
/// </summary>
public record TestCaseMessageDto(
    string Role,
    string Content,
    ToolRequestFixtureDto[] ToolRequests,
    string? ToolCallId);

/// <summary>
/// Data transfer object representing a tool request fixture.
/// </summary>
public record ToolRequestFixtureDto(string Id, string Name, string Arguments);

/// <summary>
/// Data transfer object representing a output value.
/// </summary>
public record OutputValueDto(
    string Kind,
    string? Content,
    ToolCallInfoDto? Tool,
    string? Name,
    object? Arguments
);

/// <summary>
/// Data transfer object representing a tool call info.
/// </summary>
public record ToolCallInfoDto(string Name, object Arguments);

/// <summary>
/// Data transfer object representing a model request preview.
/// </summary>
public record ModelRequestPreviewDto(
    string Model,
    RequestMessageDto[] Messages,
    RequestToolDto[] Tools);

/// <summary>
/// Data transfer object representing a request message.
/// </summary>
public record RequestMessageDto(
    string Role,
    string? Content,
    RequestToolCallDto[] ToolCalls,
    string? ToolCallId);

/// <summary>
/// Data transfer object representing a request tool call.
/// </summary>
public record RequestToolCallDto(string Id, string Name, string Arguments);

/// <summary>
/// Data transfer object representing a request tool.
/// </summary>
public record RequestToolDto(string Name, string Description, object JsonSchema);

/// <summary>
/// Data transfer object representing a evaluator fixture result.
/// </summary>
public record EvaluatorFixtureResultDto(
    string EvaluatorId,
    string EvaluatorKind,
    string EvaluatorName,
    double Score,
    bool Pass,
    BreakdownItemDto[] Breakdown,
    string Note
);

/// <summary>
/// Data transfer object representing a breakdown item.
/// </summary>
public record BreakdownItemDto(string K, string V, bool Match);

/// <summary>
/// Data transfer object representing a runtime breakdown.
/// </summary>
public record RuntimeBreakdownDto(long Total, long Ttft, long Gen, long Tools, long? Judge);

/// <summary>
/// Data transfer object representing a endpoint usage.
/// </summary>
public record EndpointUsageDto(
    string Id,
    string Label,
    string Color,
    string Region,
    double PricingIn,
    double PricingOut,
    ulong? TokIn,
    ulong? TokOut,
    ulong? CachedTokIn,
    int Calls,
    long Latency,
    double CostEur
);
