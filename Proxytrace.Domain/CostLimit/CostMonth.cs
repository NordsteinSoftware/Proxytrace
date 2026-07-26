namespace Proxytrace.Domain.CostLimit;

/// <summary>
/// The single definition of a budget period. Budgets run on the UTC calendar month and reset on
/// the 1st; nothing is cleaned up on reset because every consumer — the guard, the proxy's block
/// lookup, the Costs page — only ever queries the current month, so alerts re-arm and blocks lift
/// by themselves.
/// </summary>
/// <remarks>
/// Lives in the domain rather than the application layer because the lean proxy pipeline (which
/// never loads <c>Proxytrace.Application</c>) must derive exactly the same month key as the guard
/// that writes the breach rows.
/// </remarks>
public static class CostMonth
{
    /// <summary>Midnight UTC on the first day of the month containing <paramref name="timestamp"/>.</summary>
    public static DateTimeOffset StartOf(DateTimeOffset timestamp)
    {
        DateTimeOffset utc = timestamp.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
