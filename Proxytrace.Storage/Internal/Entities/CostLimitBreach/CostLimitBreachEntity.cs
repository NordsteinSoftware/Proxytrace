using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;

namespace Proxytrace.Storage.Internal.Entities.CostLimitBreach;

[StoredDomainEntity(typeof(ICostLimitBreach))]
internal record CostLimitBreachEntity : Entity
{
    /// <summary>
    /// Gets or sets the cost limit.
    /// </summary>
    public required Guid CostLimit { get; init; }

    /// <summary>Midnight UTC on the first of the month the breach belongs to.</summary>
    public required DateTimeOffset MonthStart { get; init; }

    /// <summary>
    /// Gets or sets the threshold.
    /// </summary>
    public required CostThreshold Threshold { get; init; }

    /// <summary>
    /// Gets or sets the spend eur.
    /// </summary>
    public required decimal SpendEur { get; init; }
}
