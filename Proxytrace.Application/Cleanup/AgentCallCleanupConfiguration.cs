namespace Proxytrace.Application.Cleanup;

/// <summary>
/// Retention settings for the agent-call (trace) history, bound from the 'AgentCallCleanup' config section.
/// </summary>
public sealed record AgentCallCleanupConfiguration
{
    /// <summary>
    /// Traces older than this are permanently deleted on each cleanup pass.
    /// </summary>
    public int RetentionDurationDays { get; init; } = 30;

    /// <summary>
    /// How often the background cleanup service scans for and removes expired traces.
    /// </summary>
    public int CleanupIntervalHours { get; init; } = 6;
}
