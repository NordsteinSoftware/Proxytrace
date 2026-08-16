using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Infrastructure.Internal;

/// <summary>
/// Resolves model prices (EUR / 1M tokens) via the LiteLLM catalog for every provider kind. Azure
/// providers (endpoint host contains <c>azure.com</c>) prefer the catalog's <c>azure/&lt;model&gt;</c>
/// entry, falling back to the bare model name. Unresolved → <see cref="ModelPrice.Unknown"/>.
/// </summary>
internal sealed class PricingService : IPricingService
{
    private readonly LiteLlmCatalogResolver liteLlmResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PricingService"/> class.
    /// </summary>
    public PricingService(LiteLlmCatalogResolver liteLlmResolver)
    {
        this.liteLlmResolver = liteLlmResolver;
    }

    /// <summary>
    /// Resolves asynchronously.
    /// </summary>
    public Task<ModelPrice> ResolveAsync(
        IModelProvider provider,
        DiscoveredModel model,
        CancellationToken cancellationToken = default)
    {
        string[] candidates = ProviderEndpoints.IsAzure(provider.Endpoint)
            ? [$"azure/{model.PricingModelName}", model.PricingModelName]
            : [model.PricingModelName];
        return liteLlmResolver.ResolveAsync(candidates, cancellationToken);
    }
}
