using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.CostLimitBreach.Internal;

internal record CostLimitBreach : DomainEntity<ICostLimitBreach>, ICostLimitBreach
{
    public ICostLimit CostLimit { get; private init; }
    public DateTimeOffset MonthStart { get; private init; }
    public CostThreshold Threshold { get; private init; }
    public decimal SpendEur { get; private init; }

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
