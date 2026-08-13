namespace Nordstein.Core.Common.Hosting;

/// <summary>
/// The application's release version (SemVer, e.g. "1.2.3"), injected at build time via
/// -p:Version from the release tag. Development builds report "0.0.0-dev".
/// </summary>
public interface IAppVersion
{
    string Version { get; }
}
