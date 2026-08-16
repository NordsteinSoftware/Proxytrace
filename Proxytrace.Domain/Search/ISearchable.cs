namespace Proxytrace.Domain.Search;

/// <summary>
/// Marks an entity as participating in the full-text search index, declaring which <see cref="SearchKind"/>
/// bucket it occupies so the indexer can route it to the correct document type.
/// </summary>
public interface ISearchable : IProjectSpecific
{
    /// <summary>The search index bucket this entity occupies (e.g. <see cref="SearchKind.Agent"/>).</summary>
    SearchKind SearchKind { get; }
}
