namespace Proxytrace.Domain.Search;

/// <summary>
/// Represents a searchable.
/// </summary>
public interface ISearchable : IProjectSpecific
{
    SearchKind SearchKind { get; }
}
