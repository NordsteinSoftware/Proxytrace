using Lucene.Net.Documents;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal.Mappers;

internal static class DocumentBuilder
{
    /// <summary>
    /// Builds.
    /// </summary>
    public static Document Build(
        SearchKind kind,
        Guid entityId,
        Guid projectId,
        DateTimeOffset createdAt,
        string title,
        string body,
        string boostedBody,
        string? metadataJson = null)
    {
        var idValue = $"{kind}:{entityId}";
        var doc = new Document
        {
            new StringField(SearchConstants.FieldId, idValue, Field.Store.YES),
            new StringField(SearchConstants.FieldKind, kind.ToString(), Field.Store.YES),
            new StringField(SearchConstants.FieldEntityId, entityId.ToString(), Field.Store.YES),
            new StringField(SearchConstants.FieldProjectId, projectId.ToString(), Field.Store.YES),
            new Int64Field(SearchConstants.FieldCreatedAt, createdAt.UtcTicks, Field.Store.YES),
            new TextField(SearchConstants.FieldTitle, title, Field.Store.YES),
            new TextField(SearchConstants.FieldBody, body, Field.Store.YES),
        };

        var boostedField = new TextField(SearchConstants.FieldBoostedBody, boostedBody, Field.Store.NO)
        {
            Boost = SearchConstants.BoostedBodyBoost,
        };
        doc.Add(boostedField);

        if (!string.IsNullOrEmpty(metadataJson))
        {
            doc.Add(new StoredField(SearchConstants.FieldMetadata, metadataJson));
        }

        return doc;
    }
}
