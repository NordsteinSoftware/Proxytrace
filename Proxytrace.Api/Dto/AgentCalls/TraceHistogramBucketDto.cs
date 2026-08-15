namespace Proxytrace.Api.Dto.AgentCalls;

/// <summary>
/// Data transfer object representing a trace histogram bucket.
/// </summary>
public record TraceHistogramBucketDto(DateTimeOffset Start, int Total, int Errors);
