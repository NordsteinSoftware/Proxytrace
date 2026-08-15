using Proxytrace.Storage.Internal;

namespace Proxytrace.Storage;

/// <summary>
/// Configuration for storage.
/// </summary>
public abstract record StorageConfiguration
{
    /// <summary>
    /// The cryptography key in base64 format
    /// </summary>
    public string? CryptographyKeyBase64 { get; private init; }
    
    /// <summary>
    /// Whether the storage supports migrations
    /// </summary>
    internal abstract bool SupportsMigrations { get; }

    /// <summary>
    /// Creates a configuration for in-memory storage
    /// </summary>
    public static StorageConfiguration InMemory()
        => new InMemoryConfiguration();

    /// <summary>
    /// Creates a configuration for PostgreSQL storage
    /// </summary>
    public static StorageConfiguration Postgres(
        string connectionString,
        string? cryptographyKeyBase64 = null)
        => new PostgresConfiguration
        {
            ConnectionString = connectionString,
            CryptographyKeyBase64 = cryptographyKeyBase64
        };
}
