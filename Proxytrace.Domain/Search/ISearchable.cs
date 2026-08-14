namespace Proxytrace.Domain.Search;

public interface ISearchable : IProjectSpecific
{
    SearchKind SearchKind { get; }
}
