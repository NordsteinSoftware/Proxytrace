using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Request payload for test connection operations.
/// </summary>
public record TestConnectionRequest(
    string ProviderName,
    string ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind);
