using Proxytrace.Domain.CostLimit;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

[StoredDomainEntity(typeof(ICostLimit))]
internal record CostLimitEntity : Entity
{
    public required Guid Project { get; init; }

    /// <summary>The scoped agent, or <c>null</c> for the project-wide limit.</summary>
    public Guid? Agent { get; init; }

    public decimal? SoftLimitEur { get; init; }

    public decimal? HardLimitEur { get; init; }

    public required bool Enabled { get; init; }
}
