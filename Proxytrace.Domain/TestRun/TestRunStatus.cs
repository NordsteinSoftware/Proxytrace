namespace Proxytrace.Domain.TestRun;

/// <summary>
/// Specifies the test run status.
/// </summary>
public enum TestRunStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}
