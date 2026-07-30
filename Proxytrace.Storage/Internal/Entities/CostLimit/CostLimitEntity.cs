using Proxytrace.Domain.CostLimit;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

[StoredDomainEntity(typeof(ICostLimit))]
internal record CostLimitEntity : Entity
{
    public required Guid Project { get; init; }

    /// <summary>The scoped agent, or <c>null</c> when the limit is not agent-scoped.</summary>
    public Guid? Agent { get; init; }

    /// <summary>The scoped inbound API key, or <c>null</c> when the limit is not key-scoped.</summary>
    public Guid? ApiKey { get; init; }

    public decimal? SoftLimitEur { get; init; }

    public decimal? HardLimitEur { get; init; }

    public required bool Enabled { get; init; }
}
