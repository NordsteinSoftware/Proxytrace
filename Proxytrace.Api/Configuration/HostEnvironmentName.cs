namespace Proxytrace.Api.Configuration;

/// <summary>
/// Resolves the environment name the <b>host</b> is running under, the way the host itself resolves
/// it.
///
/// <para>
/// <see cref="Proxytrace.Api.Module"/> builds its own <see cref="IConfiguration"/> view (the host's
/// does not read <c>appsettings.local.json</c>, which holds the generated signing key — see
/// <c>Program.cs</c>), and that second view has to agree with the host about which environment this
/// is: it decides which <c>appsettings.{Environment}.json</c> is layered in, and the session
/// cookie's <c>Secure</c> default is derived from it.
/// </para>
///
/// <para>
/// The order matters and is not the intuitive one: <c>WebApplicationBuilder</c> lets
/// <c>DOTNET_ENVIRONMENT</c> win over <c>ASPNETCORE_ENVIRONMENT</c> when both are set and disagree
/// (verified on .NET 10). Reading them the other way round meant the host ran Production while the
/// module computed Development, defaulting the 7-day session cookie's <c>Secure</c> attribute to
/// <c>false</c> on an HTTPS install.
/// </para>
///
/// <para>
/// Read from the environment rather than from configuration: the host bootstraps its environment
/// from environment variables (and the command line) before any <c>appsettings*.json</c> is layered
/// in, so an <c>ASPNETCORE_ENVIRONMENT</c> key inside a JSON file never moves the host and must not
/// move this either.
/// </para>
/// </summary>
internal static class HostEnvironmentName
{
    public const string Production = "Production";
    public const string Development = "Development";

    /// <summary>
    /// The environment name from the ambient process environment, defaulting to
    /// <see cref="Production"/>.
    /// </summary>
    public static string Resolve() => Resolve(Environment.GetEnvironmentVariable);

    /// <summary>
    /// Testable overload: <paramref name="readEnvironmentVariable"/> stands in for
    /// <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// </summary>
    /// <remarks>
    /// A variable set to an empty or whitespace value counts as unset. A container orchestrator
    /// that passes <c>ASPNETCORE_ENVIRONMENT=</c> would otherwise name a nonexistent
    /// <c>appsettings..json</c>; the host reaches the same conclusion for the only decision that
    /// depends on it here, since an empty name is not <see cref="Development"/> either way.
    /// </remarks>
    public static string Resolve(Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        return NullIfBlank(readEnvironmentVariable("DOTNET_ENVIRONMENT"))
               ?? NullIfBlank(readEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
               ?? Production;
    }

    /// <summary>True when <paramref name="environmentName"/> names the Development environment.</summary>
    public static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, Development, StringComparison.OrdinalIgnoreCase);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
