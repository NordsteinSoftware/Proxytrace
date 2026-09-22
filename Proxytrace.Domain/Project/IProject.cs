using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Project;

/// <summary>
/// Represents a project that owns agents and test suites.
/// </summary>
public interface IProject : IDomainEntity
{
    /// <summary>The display name of the project.</summary>
    string Name { get; }

    /// <summary>Provider used for anonymous pass-through, or null when disabled.</summary>
    Guid? DefaultUpstreamProviderId { get; }

    /// <summary>Returns a copy with the anonymous pass-through provider changed.</summary>
    IProject WithDefaultUpstreamProvider(Guid? providerId);

    /// <summary>
    /// Endpoint for system agents (e.g. model name generation or optimizers)
    /// </summary>
    IModelEndpoint SystemEndpoint { get; }

    /// <summary>Users that are members of this project.</summary>
    IReadOnlyCollection<IUser> Members { get; }

    /// <summary>Factory delegate for creating a new project.</summary>
    public delegate IProject CreateNew(
        string name,
        IModelEndpoint systemEndpoint,
        IReadOnlyCollection<IUser> members);

    /// <summary>Factory delegate for reconstituting an existing project from persistence.</summary>
    public delegate IProject CreateExisting(
        string name,
        IModelEndpoint systemEndpoint,
        IReadOnlyCollection<IUser> members,
        IDomainEntityData existing);
}
