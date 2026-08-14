namespace Proxytrace.Domain.Model;

/// <summary>
/// Model definition
/// </summary>
public interface IModel : IDomainEntity
{
    /// <summary>
    /// The name of the model (e.g. "claude-sonnet-4.5")
    /// </summary>
    string Name { get; }

    /// <summary>Factory delegate for creating a new model.</summary>
    public delegate IModel CreateNew(string name);

    /// <summary>Factory delegate for reconstituting an existing model from persistence.</summary>
    public delegate IModel CreateExisting(string name, IDomainEntityData existing);
}
