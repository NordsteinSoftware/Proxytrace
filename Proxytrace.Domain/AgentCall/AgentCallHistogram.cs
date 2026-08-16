namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Represents a agent call histogram bucket.
/// </summary>
public record AgentCallHistogramBucket(DateTimeOffset Start, int Total, int Errors);

/// <summary>
/// Shapes a sparse, DB-aggregated histogram into a dense, ordered array of equal-width slots
/// spanning [from, to]. The database does the heavy lifting — a single <c>GROUP BY</c> returns one
/// row per non-empty bucket as <c>(Index, Total, Errors)</c>; this fills the gaps with zeroes and
/// stamps each slot's start time. Pure and provider-agnostic so it produces identical output
/// whether the grouping ran on PostgreSQL or the in-memory provider.
/// </summary>
public static class AgentCallHistogram
{
    /// <summary>
    /// The error status threshold constant value.
    /// </summary>
    public const int ErrorStatusThreshold = 400;

    /// <summary>
    /// Expand DB-aggregated <c>(Index, Total, Errors)</c> rows into a dense <paramref name="buckets"/>-long
    /// array. Indices outside <c>[0, buckets)</c> are clamped into the nearest edge slot (a call landing
    /// exactly on <paramref name="to"/> hashes to <c>buckets</c> and folds into the last bucket).
    /// </summary>
    public static IReadOnlyList<AgentCallHistogramBucket> Expand(
        IEnumerable<(int Index, int Total, int Errors)> aggregated,
        DateTimeOffset from,
        DateTimeOffset to,
        int buckets)
    {
        if (buckets < 1) throw new ArgumentOutOfRangeException(nameof(buckets));
        if (to <= from) throw new ArgumentException("to must be after from", nameof(to));

        var totals = new int[buckets];
        var errors = new int[buckets];

        foreach (var (index, total, error) in aggregated)
        {
            var i = index;
            if (i < 0) i = 0;
            if (i >= buckets) i = buckets - 1;
            totals[i] += total;
            errors[i] += error;
        }

        var width = (to - from).Ticks / (double)buckets;
        var result = new AgentCallHistogramBucket[buckets];
        for (var i = 0; i < buckets; i++)
            result[i] = new AgentCallHistogramBucket(from.AddTicks((long)(i * width)), totals[i], errors[i]);
        return result;
    }
}
