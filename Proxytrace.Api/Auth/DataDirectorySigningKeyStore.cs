namespace Proxytrace.Api.Auth;

/// <summary>
/// Persists the generated local-auth signing key as a plain file in the application data
/// directory (PROXYTRACE_DATA_DIR). Used in container deployments, where the data directory
/// is a mounted volume — unlike the content root, it survives container recreation, so user
/// sessions stay valid across upgrades without requiring a configured signing key.
/// </summary>
internal sealed class DataDirectorySigningKeyStore : ISigningKeyStore
{
    private const string FileName = "signing-key";

    private readonly string directory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataDirectorySigningKeyStore"/> class.
    /// </summary>
    public DataDirectorySigningKeyStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    /// <summary>
    /// Registers services with the Autofac container builder.
    /// </summary>
    public string? Load()
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            return null;

        var key = File.ReadAllText(path).Trim();
        return key.Length == 0 ? null : key;
    }

    /// <summary>
    /// Persist.
    /// </summary>
    public void Persist(string signingKey)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, FileName), signingKey);
    }
}
