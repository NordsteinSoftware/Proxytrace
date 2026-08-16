namespace Proxytrace.Api.Dto.Search;

/// <summary>
/// Data transfer object representing a search indexing settings.
/// </summary>
public sealed record SearchIndexingSettingsDto(
    bool Enabled,
    IReadOnlyList<string> IndexedKinds,
    bool AutoReindexOnChange,
    int SnippetLength);
