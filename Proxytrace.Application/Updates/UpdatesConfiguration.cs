namespace Proxytrace.Application.Updates;

/// <summary>
/// Settings for the periodic update check (bound from the "Updates" config section).
/// </summary>
public sealed record UpdatesConfiguration
{
    /// <summary>
    /// Disables the update check entirely (no outbound requests) when false.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Endpoint returning the latest release, in the GitHub "releases/latest" API shape
    /// (<c>tag_name</c> + <c>html_url</c>).
    /// </summary>
    public string ManifestUrl { get; init; } = "https://api.github.com/repos/NordsteinSoftware/Proxytrace/releases/latest";

    /// <summary>
    /// How often the background service polls the manifest.
    /// </summary>
    public int CheckIntervalHours { get; init; } = 24;
}
