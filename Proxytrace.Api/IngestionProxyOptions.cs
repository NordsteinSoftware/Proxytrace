namespace Proxytrace.Api;

/// <summary>
/// Where the standalone ingestion proxy is reachable from the user's network. The UI advertises
/// this base URL (setup wizard, API-keys table, empty states) as the OpenAI <c>base_url</c>
/// clients should point at. When unset, the SPA falls back to its own origin — which is only
/// correct when a reverse proxy in front routes ingestion paths to the proxy service.
/// </summary>
public sealed record IngestionProxyOptions
{
    /// <summary>
    /// Gets or sets the public base url.
    /// </summary>
    public string? PublicBaseUrl { get; init; }
}
