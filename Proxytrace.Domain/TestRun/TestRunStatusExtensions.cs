namespace Proxytrace.Domain.TestRun;

/// <summary>
/// Canonical definition of which <see cref="TestRunStatus"/> values are terminal (no further
/// transitions), shared by the run/group state machines and every consumer that filters
/// finished runs — so they can never drift apart.
/// </summary>
public static class TestRunStatusExtensions
{
    /// <summary>
    /// Determines whether the terminal.
    /// </summary>
    public static bool IsTerminal(this TestRunStatus status)
        => status is TestRunStatus.Completed or TestRunStatus.Failed or TestRunStatus.Cancelled;
}
