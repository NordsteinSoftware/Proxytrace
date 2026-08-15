namespace Proxytrace.Domain.ModelProvider;

/// <summary>Resolved per-model price in EUR per 1M tokens; nulls when unresolved.</summary>
public record ModelPrice(decimal? InputTokenCost, decimal? OutputTokenCost, decimal? CachedInputTokenCost = null)
{
    /// <summary>
    /// The unknown.
    /// </summary>
    public static readonly ModelPrice Unknown = new(null, null, null);
}
