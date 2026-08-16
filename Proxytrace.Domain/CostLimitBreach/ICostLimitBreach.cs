using Proxytrace.Domain.CostLimit;

namespace Proxytrace.Domain.CostLimitBreach;

/// <summary>
/// The persisted record that one threshold of one <see cref="ICostLimit"/> was crossed in one
/// calendar month. The row's mere presence is the state: a <c>Soft</c> row means the warning has
/// already fired (so it fires once per month), a <c>Hard</c> row means the proxy blocks the scope
/// for the rest of that month.
/// </summary>
/// <remarks>
/// Breach state lives in its own entity rather than on <see cref="ICostLimit"/> so the background
/// guard's writes never race a user editing the thresholds. There is no cleanup: the guard and the
/// proxy only ever query the current month, so alerts re-arm and blocks lift on the 1st by
/// themselves. Rows deliberately survive retention pruning that drops month-to-date spend back
/// below a fired threshold — a breach is a historical fact and is never un-fired.
/// </remarks>
public interface ICostLimitBreach : IDomainEntity<ICostLimitBreach>
{
    /// <summary>The limit whose threshold was crossed.</summary>
    ICostLimit CostLimit { get; }

    /// <summary>Midnight UTC on the first of the calendar month the breach belongs to.</summary>
    DateTimeOffset MonthStart { get; }

    /// <summary>Which of the limit's two thresholds was crossed.</summary>
    CostThreshold Threshold { get; }

    /// <summary>The month-to-date spend in EUR measured at the moment of the crossing.</summary>
    decimal SpendEur { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ICostLimitBreach CreateNew(
        ICostLimit costLimit,
        DateTimeOffset monthStart,
        CostThreshold threshold,
        decimal spendEur);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ICostLimitBreach CreateExisting(
        ICostLimit costLimit,
        DateTimeOffset monthStart,
        CostThreshold threshold,
        decimal spendEur,
        IDomainEntityData existing);
}
