using Proxytrace.Domain.Scope;

namespace Proxytrace.Storage.Internal.Entities.Scope;

[StoredDomainEntity(typeof(IScope))]
internal record ScopeEntity : Entity
{
    /// <summary>
    /// Gets or sets the canonical external key (the URL segment).
    /// </summary>
    public required string ExternalKey { get; init; }
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; init; }
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string? Description { get; init; }
}
