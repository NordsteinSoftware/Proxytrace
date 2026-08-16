using Proxytrace.Domain.Project;

namespace Proxytrace.Domain;

/// <summary>
/// Marks an entity as belonging to a specific <see cref="IProject"/>, enabling project-scoped
/// authorization checks, search indexing, and cost attribution throughout the domain.
/// </summary>
public interface IProjectSpecific
{
    /// <summary>The project that owns this entity.</summary>
    IProject Project { get; }
}
