namespace Proxytrace.Api.Dto.AgentCalls;

/// <summary>
/// Aggregate over every trace matching the traces filter — backs the KPI band above the trace list.
/// Deliberately unpaged: the list scrolls rather than pages, so its KPIs describe the whole filtered
/// set rather than a slice of it.
/// </summary>
/// <param name="TotalCostEur">
/// Null when no matching trace had a known price, which is a different fact from a genuine zero
/// (a free or self-hosted model) — the UI renders the two differently.
/// </param>
public sealed record AgentCallSummaryDto(
    int Count,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    double? TotalCostEur,
    double AvgLatencyMs,
    double LatencyStdDevMs,
    int ErrorCount);
