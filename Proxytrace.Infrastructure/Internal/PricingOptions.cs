namespace Proxytrace.Infrastructure.Internal;

/// <summary>Pricing feed endpoints. Defaults are baked in; override via the "Pricing" config section.</summary>
public sealed class PricingOptions
{
    /// <summary>
    /// Gets or sets the lite llm feed url.
    /// </summary>
    public string LiteLlmFeedUrl { get; init; } =
        "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json";
    /// <summary>
    /// Gets or sets the fx api url.
    /// </summary>
    public string FxApiUrl { get; init; } = "https://api.frankfurter.app/latest";
}
