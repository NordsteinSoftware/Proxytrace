namespace Proxytrace.Client.Sample;

public record Configuration
{
    public required string Endpoint { get; init; }
    public required string ApiKey { get; init; }
}
