namespace Proxytrace.Domain.ModelProvider;

/// <summary>
/// Client for communicating with the provider endpoint.
/// </summary>
public interface IProviderClient
{
    /// <summary>
    /// Encapsulates a factory operation.
    /// </summary>
    public delegate IProviderClient Factory(IModelProvider provider);

    Task<ProviderConnectionResult> VerifyConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the provider's models and resolves each one's price. For Azure providers only the
    /// deployed models are returned (never the full upstream model list).
    /// </summary>
    Task<IReadOnlyList<PricedModel>> GetModelsAsync(CancellationToken cancellationToken = default);
}
