using Proxytrace.Domain.Session;

namespace Proxytrace.Storage.Internal.Entities.Session;

[StoredDomainEntity(typeof(ISession))]
internal record SessionEntity : Entity
{
    public required string ExternalKey { get; init; }
    public required Guid ProjectId { get; init; }
    public required DateTimeOffset LastActivityAt { get; init; }
    public required int TraceCount { get; init; }
    public required long TotalTokens { get; init; }
}
