using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.AgentCall;

/// <summary>
/// Per-endpoint aggregate slice of a filtered trace set. The repository returns one of these per
/// distinct endpoint in the result because cost is priced per endpoint
/// (<see cref="IModelEndpoint.CalculateCost"/>) and so cannot be summed in SQL. Latency arrives as
/// sum + sum-of-squares + count so the standard deviation can be derived without a
/// <c>stddev_samp</c> the provider cannot translate.
/// </summary>
/// <param name="LatencyCount">
/// How many calls in this group actually recorded a latency — the divisor for the mean. It is
/// distinct from <paramref name="Count"/>, which includes calls whose latency is unknown.
/// </param>
public sealed record AgentCallSummaryGroup(
    Guid EndpointId,
    int Count,
    ulong InputTokens,
    ulong OutputTokens,
    ulong CachedInputTokens,
    double LatencySum,
    double LatencySumOfSquares,
    int LatencyCount,
    int ErrorCount);

/// <summary>
/// Aggregate over every trace matching a filter — what the traces KPI band displays. Built by
/// <see cref="Fold"/> from the per-endpoint groups the repository returns. Deliberately unpaged:
/// the traces table scrolls rather than paging, so its KPIs describe the whole filtered set.
/// </summary>
public sealed record AgentCallSummary(
    int Count,
    ulong InputTokens,
    ulong OutputTokens,
    ulong CachedInputTokens,
    decimal? TotalCost,
    double AvgLatencyMs,
    double LatencyStdDevMs,
    int ErrorCount)
{
    /// <summary>Nothing matched the filter. <see cref="TotalCost"/> is null, not zero.</summary>
    public static AgentCallSummary Empty { get; } = new(0, 0, 0, 0, null, 0, 0, 0);

    /// <summary>
    /// Folds the per-endpoint groups into one summary, pricing each group with its own endpoint.
    /// <see cref="IModelEndpoint.CalculateCost"/> is linear in each token count (the same property
    /// <c>TestRunTotals</c> relies on), so pricing a group's summed tokens equals summing the costs
    /// of its individual calls.
    /// <para>
    /// <paramref name="endpointLookup"/> returns <see langword="null"/> for an endpoint that no
    /// longer exists; such a group still contributes tokens, counts and latency — just no cost.
    /// A null <see cref="TotalCost"/> therefore means "no matching call had a known price", which
    /// is a different fact from a genuine zero (a free or self-hosted model).
    /// </para>
    /// </summary>
    public static AgentCallSummary Fold(
        IEnumerable<AgentCallSummaryGroup> groups,
        Func<Guid, IModelEndpoint?> endpointLookup)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(endpointLookup);

        int count = 0;
        ulong input = 0;
        ulong output = 0;
        ulong cached = 0;
        double latencySum = 0;
        double latencySumOfSquares = 0;
        int latencyCount = 0;
        int errorCount = 0;
        decimal? cost = null;

        foreach (var group in groups)
        {
            count += group.Count;
            input += group.InputTokens;
            output += group.OutputTokens;
            cached += group.CachedInputTokens;
            latencySum += group.LatencySum;
            latencySumOfSquares += group.LatencySumOfSquares;
            latencyCount += group.LatencyCount;
            errorCount += group.ErrorCount;

            if (endpointLookup(group.EndpointId) is { } endpoint &&
                endpoint.CalculateCost(
                    new TokenUsage(group.InputTokens, group.OutputTokens, group.CachedInputTokens)) is { } groupCost)
            {
                cost = (cost ?? 0m) + groupCost;
            }
        }

        if (count == 0)
        {
            return Empty;
        }

        double average = latencyCount > 0 ? latencySum / latencyCount : 0;

        return new AgentCallSummary(
            Count: count,
            InputTokens: input,
            OutputTokens: output,
            CachedInputTokens: cached,
            TotalCost: cost,
            AvgLatencyMs: average,
            LatencyStdDevMs: StdDev(latencySum, latencySumOfSquares, latencyCount),
            ErrorCount: errorCount);
    }

    /// <summary>
    /// Population standard deviation derived from three running scalars, so the database never has
    /// to compute one.
    /// <para>
    /// Population, not sample: this aggregate covers <i>every</i> call matching the filter, so the
    /// rows are the whole population rather than a draw from one. It also matches what the traces
    /// KPI band displayed when it was computed client-side, so the "±" figure does not silently
    /// change meaning.
    /// </para>
    /// <para>
    /// Returns zero for fewer than two values, and clamps a negative variance to zero: when every
    /// value is identical, <c>sumOfSquares - sum²/n</c> can land marginally below zero in floating
    /// point, and a square root of that would surface NaN in the UI.
    /// </para>
    /// </summary>
    public static double StdDev(double sum, double sumOfSquares, int n)
    {
        if (n < 2)
        {
            return 0;
        }

        double variance = (sumOfSquares - sum * sum / n) / n;
        return variance <= 0 ? 0 : Math.Sqrt(variance);
    }
}
