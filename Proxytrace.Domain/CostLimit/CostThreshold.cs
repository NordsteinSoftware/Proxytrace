namespace Proxytrace.Domain.CostLimit;

/// <summary>
/// Which of a cost limit's two thresholds a breach refers to. Persisted as an int — append only.
/// </summary>
public enum CostThreshold
{
    /// <summary>The advisory threshold: notifies, never blocks.</summary>
    Soft = 0,

    /// <summary>The enforcing threshold: notifies and blocks proxied calls until the month resets.</summary>
    Hard = 1,
}
