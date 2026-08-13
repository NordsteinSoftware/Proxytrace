using System.Reflection;

namespace Nordstein.Core.Common.Hosting.Internal;

/// <summary>
/// Reads the version from this assembly's <see cref="AssemblyInformationalVersionAttribute"/>,
/// which carries the full SemVer (including prerelease suffixes) that MSBuild derives from
/// the -p:Version build property. Solution-wide the property comes from Directory.Build.props,
/// so any assembly in the solution carries the same value regardless of the host process.
/// </summary>
internal sealed class AppVersion : IAppVersion
{
    public string Version { get; }

    public AppVersion()
    {
        string? informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // Defensive: strip "+<build-metadata>" should a build re-enable source-revision stamping.
        Version = string.IsNullOrWhiteSpace(informational) ? "0.0.0-dev" : informational.Split('+')[0];
    }
}
