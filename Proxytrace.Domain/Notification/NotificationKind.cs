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
    /// The installation has reached its licensed monthly trace limit and captures for this project
    /// are being dropped.
    /// </summary>
    /// <remarks>
    /// A dropped capture is still acknowledged to the client — failing the proxied call would take
    /// the caller's application down over a billing limit — so without this notification the only
    /// symptom was traces quietly going missing.
    /// </remarks>
    TraceQuotaReached,
}
