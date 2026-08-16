using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Outliers;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.AgentVersion;

namespace Proxytrace.Storage.Internal.Statistics;

/// <summary>
/// Computes an agent's recent per-call metric baselines for the ingestion outlier detector. Loads the
/// agent's last N successful calls (bounded by <c>sampleWindow</c>) and computes mean/stddev in C# — no
/// SQL ordered-set aggregate, so the same path runs on the relational and in-memory providers.
/// </summary>
[UsedImplicitly]
internal sealed class OutlierBaselineQueries : IOutlierBaselineReader
{
    private readonly Func<StorageDbContext> contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutlierBaselineQueries"/> class.
    /// </summary>
    public OutlierBaselineQueries(Func<StorageDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// Gets the baseline asynchronously.
    /// </summary>
    public async Task<OutlierBaseline> GetBaselineAsync(
        Guid agentId, int sampleWindow, CancellationToken cancellationToken = default)
    {
        if (sampleWindow <= 0)
        {
            return OutlierBaseline.Empty;
        }

        StorageDbContext context = contextFactory();

        IQueryable<Guid> versionIdsForAgent = context.Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .Select(v => v.Id);

        // Most recent successful calls only, capped at the window. Take is provider-portable (unlike a
        // SQL STDDEV/percentile), so the in-memory provider (kiosk/tests) runs the identical path.
        var calls = await context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(c => versionIdsForAgent.Contains(c.AgentVersionId)
                && c.HttpStatus >= 200 && c.HttpStatus < 300)
            .OrderByDescending(c => c.CreatedAt)
            .Take(sampleWindow)
            .Select(c => new BaselineRow(
                c.ConversationId ?? c.Id,
                c.CreatedAt,
                c.InputTokens,
                c.OutputTokens,
                c.CachedInputTokens,
                c.LatencyMs,
                c.ResponseToolRequestCount))
            .ToListAsync(cancellationToken);

        if (calls.Count == 0)
        {
            return OutlierBaseline.Empty;
        }

        double[] tokenSamples = calls
            .Select(c => (double)((c.InputTokens ?? 0UL) + (c.OutputTokens ?? 0UL)))
            .ToArray();
        double[] latencySamples = calls
            .Where(c => c.LatencyMs != null)
            .Select(c => c.LatencyMs ?? 0d)
            .ToArray();
        double[] toolSamples = calls.Select(c => (double)c.ToolCount).ToArray();

        // Cache-hit per call for turn ≥ 2: within the window, a call is turn ≥ 2 when an earlier call
        // shares its conversation (drop the earliest per conversation). Sample cached ÷ input when input
        // was sent — matches how the current call's cache-hit is computed at ingestion.
        var cacheSamples = new List<double>();
        foreach (var conversation in calls.GroupBy(c => c.ConvKey))
        {
            foreach (BaselineRow later in conversation.OrderBy(c => c.CreatedAt).Skip(1))
            {
                long input = (long)(later.InputTokens ?? 0UL);
                if (input > 0L)
                {
                    cacheSamples.Add(Math.Clamp((long)(later.CachedInputTokens ?? 0UL) / (double)input, 0d, 1d));
                }
            }
        }

        return new OutlierBaseline(
            TotalTokens: MeanStdDev(tokenSamples),
            LatencyMs: MeanStdDev(latencySamples),
            CacheHitRate: MeanStdDev(cacheSamples),
            ToolCalls: MeanStdDev(toolSamples));
    }

    /// <summary>Mean and sample (n−1) standard deviation; stddev is 0 with fewer than two samples.</summary>
    private static MetricBaseline MeanStdDev(IReadOnlyList<double> samples)
    {
        int n = samples.Count;
        if (n == 0)
        {
            return default;
        }

        double mean = samples.Sum() / n;
        double std = 0d;
        if (n >= 2)
        {
            double sumSq = 0d;
            foreach (double v in samples)
            {
                double d = v - mean;
                sumSq += d * d;
            }
            std = Math.Sqrt(sumSq / (n - 1));
        }

        return new MetricBaseline(mean, std, n);
    }

    private sealed record BaselineRow(
        Guid ConvKey,
        DateTimeOffset CreatedAt,
        ulong? InputTokens,
        ulong? OutputTokens,
        ulong? CachedInputTokens,
        double? LatencyMs,
        int ToolCount);
}
