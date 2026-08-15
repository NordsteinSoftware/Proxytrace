using Proxytrace.Domain.TestResult;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.TestRun;

/// <summary>
/// Aggregated token usage and cost across the test results of an <see cref="ITestRun"/>.
/// </summary>
public record TestRunTotals(decimal? CostEur, long? TokensIn, long? TokensOut, long? CachedTokensIn)
{
    /// <summary>
    /// From.
    /// </summary>
    public static TestRunTotals From(ITestRun run)
    {
        var usages = run.TestResults
            .Select(r => r.Usage)
            .OfType<TokenUsage>()
            .ToArray();
        if (usages.Length == 0)
            return new TestRunTotals(null, null, null, null);

        ulong tokensIn = 0;
        ulong tokensOut = 0;
        ulong cachedTokensIn = 0;
        foreach (var usage in usages)
        {
            tokensIn += usage.InputTokenCount;
            tokensOut += usage.OutputTokenCount;
            cachedTokensIn += usage.CachedInputTokenCount;
        }

        var cost = run.Endpoint.CalculateCost(new TokenUsage(tokensIn, tokensOut, cachedTokensIn));
        return new TestRunTotals(cost, (long)tokensIn, (long)tokensOut, (long)cachedTokensIn);
    }

    /// <summary>
    /// Token usage and cost of a single <see cref="ITestResult"/> (one LLM call), priced by the run's
    /// endpoint. Used to put live per-case totals on the SSE <c>test-result-arrived</c> event and on
    /// each <see cref="ITestResult"/> projection so the UI can sum a running run's cost/tokens as each
    /// case lands. <c>CalculateCost</c> is linear, so summing these per-case totals equals
    /// <see cref="From"/> over the whole run.
    /// </summary>
    public static TestRunTotals FromResult(ITestRun run, ITestResult result)
    {
        if (result.Usage is not { } usage)
            return new TestRunTotals(null, null, null, null);

        var cost = run.Endpoint.CalculateCost(usage);
        return new TestRunTotals(
            cost,
            (long)usage.InputTokenCount,
            (long)usage.OutputTokenCount,
            (long)usage.CachedInputTokenCount);
    }
}
