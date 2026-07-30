namespace Proxytrace.Application.TestRun;

public sealed class TestRunnerConfiguration
{
    /// <summary>
    /// Maximum number of upstream model calls the test runner keeps in flight at once, across every
    /// running group, run, test case and agentic evaluator. Default is 2.
    /// </summary>
    /// <remarks>
    /// This is an absolute cap, not a per-level one. The runner nests three parallel loops (runs →
    /// test cases → evaluators); applying this value to each of them separately would multiply it,
    /// so a setting of 2 would allow 8 concurrent calls. <c>TestRunnerService</c> enforces the value
    /// with a single semaphore around the calls that leave the process, which is what governs
    /// provider rate limits and spend.
    /// </remarks>
    public int MaxDegreeOfParallelism { get; init; } = 2;
}

