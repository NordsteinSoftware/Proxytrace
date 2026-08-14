using Proxytrace.Domain.Project;

namespace Proxytrace.Domain;

/// <summary>
/// Something that has a relation to a <see cref="IProject"/>
/// </summary>
public interface IProjectSpecific 
{
    IProject Project { get; }
}
