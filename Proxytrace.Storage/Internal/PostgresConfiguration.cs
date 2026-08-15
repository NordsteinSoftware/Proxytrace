namespace Proxytrace.Storage.Internal;

internal record PostgresConfiguration : StorageConfiguration
{
    internal override bool SupportsMigrations => true;

    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public required string ConnectionString { get; init; }
}
