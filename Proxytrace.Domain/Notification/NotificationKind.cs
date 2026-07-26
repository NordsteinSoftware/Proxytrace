namespace Proxytrace.Domain.Notification;

/// <summary>
/// What a <see cref="INotification"/> represents. The notification table is multi-purpose:
/// the same entity, stream and dashboard section serve every kind. Add new kinds here as the
/// notification surface grows (e.g. an export finishing, a quota threshold reached).
/// </summary>
public enum NotificationKind
{
    /// <summary>A detected negative anomaly in a test run (failure, pass-rate drop, latency spike).</summary>
    Anomaly,

    /// <summary>An optimization proposal has been generated and is awaiting review.</summary>
    ProposalReady,

    /// <summary>
    /// A configured monthly cost budget crossed one of its thresholds. The severity distinguishes
    /// the two: <c>Warning</c> for the soft limit, <c>Critical</c> for the hard limit (which also
    /// blocks further proxied calls for the rest of the month).
    /// </summary>
    CostBudget,
}
