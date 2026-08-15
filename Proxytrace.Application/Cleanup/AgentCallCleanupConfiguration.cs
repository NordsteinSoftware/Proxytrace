namespace Proxytrace.Application.Cleanup;

/// <summary>
/// Configuration for agent call cleanup.
/// </summary>
public sealed record AgentCallCleanupConfiguration
{
    /// <summary>
    /// Gets or sets the retention duration days.
    /// </summary>
    public int RetentionDurationDays { get; init; } = 30;
    /// <summary>
    /// Gets or sets the cleanup interval hours.
    /// </summary>
    public int CleanupIntervalHours { get; init; } = 6;
}
