using Proxytrace.Domain.Project;

namespace Proxytrace.Storage.Internal.Entities.Project;

[StoredDomainEntity(typeof(IProject))]
[Cacheable]
internal record ProjectEntity : Entity
{
    /// <summary>
    /// <see cref="Proxytrace.Domain.Project.IProject.Name"/>
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// <see cref="Proxytrace.Domain.Project.IProject.SystemEndpoint"/>
    /// </summary>
    public required Guid SystemEndpoint { get; init; }

    public Guid? DefaultUpstreamProviderId { get; init; }

    /// <summary>
    /// Junction rows for the project's members.
    /// </summary>
    public required ICollection<ProjectUserEntity> ProjectUsers { get; init; }
}
