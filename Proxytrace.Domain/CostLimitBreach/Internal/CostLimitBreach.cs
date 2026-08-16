using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.CostLimit;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.CostLimitBreach.Internal;

internal record CostLimitBreach : DomainEntity<ICostLimitBreach>, ICostLimitBreach
{
    /// <summary>
    /// Gets or sets the cost limit.
    /// </summary>
    public ICostLimit CostLimit { get; private init; }
    /// <summary>
    /// Gets or sets the month start.
    /// </summary>
    public DateTimeOffset MonthStart { get; private init; }
    /// <summary>
    /// Gets or sets the threshold.
    /// </summary>
    public CostThreshold Threshold { get; private init; }
    /// <summary>
    /// Gets or sets the spend eur.
    /// </summary>
    public decimal SpendEur { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CostLimitBreach"/> class.
    /// </summary>
    public CostLimitBreach(
        ICostLimit costLimit,
        DateTimeOffset monthStart,
        CostThreshold threshold,
        decimal spendEur,
        IRepository<ICostLimitBreach> repository) : base(repository)
    {
        CostLimit = costLimit;
        MonthStart = monthStart;
        Threshold = threshold;
        SpendEur = spendEur;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CostLimitBreach"/> class.
    /// </summary>
    public CostLimitBreach(
        ICostLimit costLimit,
        DateTimeOffset monthStart,
        CostThreshold threshold,
        decimal spendEur,
        IDomainEntityData existing,
        IRepository<ICostLimitBreach> repository) : base(existing, repository)
    {
        CostLimit = costLimit;
        MonthStart = monthStart;
        Threshold = threshold;
        SpendEur = spendEur;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.NotNull(CostLimit);
        yield return Validation.Defined(Threshold);
        yield return Validation.NotNegative(SpendEur);

        // The month key is what makes "fired this month" queryable; a mid-month timestamp would
        // silently split one month into two buckets and let an alert fire twice.
        if (MonthStart != NormalizeToMonthStart(MonthStart))
            yield return new ValidationResult(
                "MonthStart must be midnight UTC on the first day of a month.",
                [nameof(MonthStart)]);
    }

    private static DateTimeOffset NormalizeToMonthStart(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
