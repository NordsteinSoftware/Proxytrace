namespace Proxytrace.Application.Playground.Internal;

/// <summary>
/// Event raised when a playground occurs.
/// </summary>
public abstract record PlaygroundEvent;

/// <summary>
/// Event raised when a token occurs.
/// </summary>
public sealed record TokenEvent(string Delta) : PlaygroundEvent;

/// <summary>
/// Event raised when a tool request occurs.
/// </summary>
public sealed record ToolRequestEvent(string Id, string Name, string Arguments) : PlaygroundEvent;

/// <summary>
/// Event raised when a done occurs.
/// </summary>
public sealed record DoneEvent(
    ulong InputTokens,
    ulong OutputTokens,
    ulong CachedInputTokens,
    long LatencyMs,
    decimal? CostEur,
    string? FinishReason) : PlaygroundEvent;

/// <summary>
/// Event raised when a error occurs.
/// </summary>
public sealed record ErrorEvent(string Message) : PlaygroundEvent;
