namespace Proxytrace.Client.Sample;

/// <summary>
/// Configuration for .
/// </summary>
public record Configuration
{
    /// <summary>
    /// Gets or sets the endpoint.
    /// </summary>
    public required string Endpoint { get; init; }
    /// <summary>
    /// Gets or sets the api key.
    /// </summary>
    public required string ApiKey { get; init; }
}
