namespace Proxytrace.Storage.Internal.Entities.Licensing;

/// <summary>
/// The runtime-set license JWT (set via the setup wizard or the settings UI). A single-row
/// table: at most one license is stored per installation.
/// </summary>
internal record StoredLicenseEntity : Entity
{
    /// <summary>
    /// Gets or sets the jwt.
    /// </summary>
    public required string Jwt { get; init; }
}
