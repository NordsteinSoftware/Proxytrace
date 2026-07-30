using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;

namespace Proxytrace.Storage.Internal.Entities.CostLimitBreach;

[StoredDomainEntity(typeof(ICostLimitBreach))]
internal record CostLimitBreachEntity : Entity
{
    public required Guid CostLimit { get; init; }

    /// <summary>Midnight UTC on the first of the month the breach belongs to.</summary>
    public required DateTimeOffset MonthStart { get; init; }

    public required CostThreshold Threshold { get; init; }

    public required decimal SpendEur { get; init; }
}
