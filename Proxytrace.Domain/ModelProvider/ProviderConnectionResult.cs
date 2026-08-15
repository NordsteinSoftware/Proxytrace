namespace Proxytrace.Domain.ModelProvider;

/// <summary>
/// Encapsulates the result of a provider connection operation.
/// </summary>
public record ProviderConnectionResult(
    bool Success,
    ProviderConnectionError? Error,
    int ModelCount);
