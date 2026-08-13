using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Common.Hosting;
using Nordstein.Core.Common.Time;

namespace Proxytrace.Application.Updates.Internal;

/// <summary>
/// Periodically polls the release manifest and exposes whether a newer version than the
/// running one is available. Purely informational: every failure is swallowed (logged at
/// debug) and never affects application health. Disabled in kiosk mode and for dev builds
/// (version 0.0.0-dev), which would otherwise always see an "update".
/// </summary>
internal sealed class UpdateCheckService : BackgroundService, IUpdateService
{
    internal const string HttpClientName = "updates";

    private readonly UpdatesConfiguration configuration;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IAppVersion appVersion;
    private readonly KioskOptions kioskOptions;
    private readonly IClock clock;
    private readonly ILogger<UpdateCheckService> logger;

    private volatile UpdateStatus current;

    public UpdateCheckService(
        UpdatesConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IAppVersion appVersion,
        KioskOptions kioskOptions,
        IClock clock,
        ILogger<UpdateCheckService> logger)
    {
        this.configuration = configuration;
        this.httpClientFactory = httpClientFactory;
        this.appVersion = appVersion;
        this.kioskOptions = kioskOptions;
        this.clock = clock;
        this.logger = logger;

        current = new UpdateStatus(
            CurrentVersion: appVersion.Version,
            LatestVersion: null,
            UpdateAvailable: false,
            ReleaseUrl: null,
            CheckedAt: null);
    }

    public UpdateStatus Current => current;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!configuration.Enabled || kioskOptions.Enabled)
            return;

        if (appVersion.Version.StartsWith("0.0.0", StringComparison.Ordinal))
        {
            logger.LogDebug("Update check disabled for dev build {Version}", appVersion.Version);
            return;
        }

        var period = TimeSpan.FromHours(Math.Max(1, configuration.CheckIntervalHours));

        // Small startup delay so the check never competes with boot work (migrations, seeding).
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckOnceAsync(cancellationToken);

            try
            {
                await Task.Delay(period, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(HttpClientName);
            var manifest = await client.GetFromJsonAsync<ReleaseManifest>(configuration.ManifestUrl, cancellationToken);

            var latest = manifest?.TagName?.TrimStart('v');
            if (string.IsNullOrWhiteSpace(latest))
                return;

            current = new UpdateStatus(
                CurrentVersion: appVersion.Version,
                LatestVersion: latest,
                UpdateAvailable: IsNewer(latest, appVersion.Version),
                ReleaseUrl: manifest?.HtmlUrl,
                CheckedAt: clock.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Update check failed; keeping previous status");
        }
    }

    /// <summary>
    /// SemVer comparison: numeric major/minor/patch, a release ranking above any prerelease
    /// of the same triple, prereleases compared ordinally. Unparseable versions never report
    /// an update.
    /// </summary>
    internal static bool IsNewer(string candidate, string current)
    {
        if (!TryParse(candidate, out var c) || !TryParse(current, out var r))
            return false;

        int numeric = (c.Major, c.Minor, c.Patch).CompareTo((r.Major, r.Minor, r.Patch));
        if (numeric != 0)
            return numeric > 0;

        if (c.Prerelease is null)
            return r.Prerelease is not null;

        return r.Prerelease is not null && string.CompareOrdinal(c.Prerelease, r.Prerelease) > 0;
    }

    private static bool TryParse(string version, out (int Major, int Minor, int Patch, string? Prerelease) parsed)
    {
        parsed = default;

        string core = version;
        string? prerelease = null;
        int dash = version.IndexOf('-');
        if (dash >= 0)
        {
            core = version[..dash];
            prerelease = version[(dash + 1)..];
        }

        string[] parts = core.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out int major)
            || !int.TryParse(parts[1], out int minor)
            || !int.TryParse(parts[2], out int patch))
        {
            return false;
        }

        parsed = (major, minor, patch, prerelease);
        return true;
    }

    private sealed record ReleaseManifest(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
