namespace Proxytrace.Application.Search.Internal;

internal static class SearchConstants
{
    /// <summary>
    /// The field id constant value.
    /// </summary>
    public const string FieldId = "id";
    /// <summary>
    /// The field kind constant value.
    /// </summary>
    public const string FieldKind = "kind";
    /// <summary>
    /// The field entity id constant value.
    /// </summary>
    public const string FieldEntityId = "entityId";
    /// <summary>
    /// The field project id constant value.
    /// </summary>
    public const string FieldProjectId = "projectId";
    /// <summary>
    /// The field created at constant value.
    /// </summary>
    public const string FieldCreatedAt = "createdAt";
    /// <summary>
    /// The field title constant value.
    /// </summary>
    public const string FieldTitle = "title";
    /// <summary>
    /// The field body constant value.
    /// </summary>
    public const string FieldBody = "body";
    /// <summary>
    /// The field boosted body constant value.
    /// </summary>
    public const string FieldBoostedBody = "boostedBody";
    /// <summary>
    /// The field metadata constant value.
    /// </summary>
    public const string FieldMetadata = "metadata";

    /// <summary>
    /// The boosted body boost constant value.
    /// </summary>
    public const float BoostedBodyBoost = 2.0f;
}
