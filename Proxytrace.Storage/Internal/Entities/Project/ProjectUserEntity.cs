namespace Proxytrace.Storage.Internal.Entities.Project;

/// <summary>
/// Join table entity for the many-to-many relationship between Projects and Users.
/// This is a storage-only entity with no domain counterpart.
/// </summary>
internal record ProjectUserEntity
{
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
    /// <summary>
    /// Gets or sets the user id.
    /// </summary>
    public required Guid UserId { get; init; }
}
