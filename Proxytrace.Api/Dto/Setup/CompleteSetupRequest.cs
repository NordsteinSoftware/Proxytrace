using Proxytrace.Domain.ModelProvider;

namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Request payload for complete setup operations.
/// </summary>
public record CompleteSetupRequest(
    string ProviderName,
    string ProviderEndpoint,
    string ProviderUpstreamApiKey,
    ModelProviderKind ProviderKind,
    string ModelName,
    string ProjectName);
