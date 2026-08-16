namespace Proxytrace.Api.Dto.Setup;

/// <summary>
/// Data transfer object representing a setup status.
/// </summary>
public record SetupStatusDto
{
    /// <summary>
    /// Gets or sets the is configured.
    /// </summary>
    public required bool IsConfigured { get; init; }
}
