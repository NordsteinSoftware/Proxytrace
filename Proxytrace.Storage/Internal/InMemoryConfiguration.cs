namespace Proxytrace.Storage.Internal;

/// <summary>
/// Configuration for in-memory storage
/// </summary>
internal record InMemoryConfiguration : StorageConfiguration
{
    /// <inheritdoc />
    internal override bool SupportsMigrations => false;
    
    /// <summary>
    /// The name of the in-memory storage instance
    /// </summary>
    public string Name { get; } = Guid.NewGuid().ToString();
}
