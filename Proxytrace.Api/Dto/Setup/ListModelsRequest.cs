using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Request payload for list models operations.
/// </summary>
public record ListModelsRequest(
    string ProviderName,
    string ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind);
