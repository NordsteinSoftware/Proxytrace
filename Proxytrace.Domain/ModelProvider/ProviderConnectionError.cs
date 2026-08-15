namespace Proxytrace.Domain.ModelProvider;

/// <summary>
/// Specifies the provider connection error.
/// </summary>
public enum ProviderConnectionError
{
    Unauthorized,
    NetworkError,
    UnsupportedKind,
    Unknown,
}
