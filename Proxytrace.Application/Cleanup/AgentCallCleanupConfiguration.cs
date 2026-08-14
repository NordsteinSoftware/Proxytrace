namespace Proxytrace.Application.Cleanup;

public sealed record AgentCallCleanupConfiguration
{
    public int RetentionDurationDays { get; init; } = 30;
    public int CleanupIntervalHours { get; init; } = 6;
}
