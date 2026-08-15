using Proxytrace.Domain.Session;

namespace Proxytrace.Storage.Internal.Entities.Session;

[StoredDomainEntity(typeof(ISession))]
internal record SessionEntity : Entity
{
    /// <summary>
    /// Gets or sets the external key.
    /// </summary>
    public required string ExternalKey { get; init; }
    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public required Guid ProjectId { get; init; }
    /// <summary>
    /// Gets or sets the last activity at.
    /// </summary>
    public required DateTimeOffset LastActivityAt { get; init; }
    /// <summary>
    /// Gets or sets the trace count.
    /// </summary>
    public required int TraceCount { get; init; }
    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    public required long TotalTokens { get; init; }
}
