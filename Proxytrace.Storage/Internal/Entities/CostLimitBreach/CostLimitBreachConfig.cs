using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;

namespace Proxytrace.Storage.Internal.Entities.CostLimitBreach;

internal class CostLimitBreachConfig
    : AbstractEntityConfiguration<CostLimitBreachEntity>,
      IMapper<ICostLimitBreach, CostLimitBreachEntity>
{
    private readonly IRepository<ICostLimit> costLimits;
    private readonly ICostLimitBreach.CreateExisting factory;

    public CostLimitBreachConfig(
        IRepository<ICostLimit> costLimits,
        ICostLimitBreach.CreateExisting factory)
    {
        this.costLimits = costLimits;
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<CostLimitBreachEntity> builder)
    {
        builder
            .HasOne<Entities.CostLimit.CostLimitEntity>()
            .WithMany()
            .HasForeignKey(e => e.CostLimit)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.SpendEur).HasPrecision(18, 6);

        // Makes a concurrent double-fire of the same threshold impossible at the database level,
        // so "has this alert already fired this month" never depends on read-then-write timing.
        builder
            .HasIndex(e => new { e.CostLimit, e.MonthStart, e.Threshold })
            .IsUnique();

        // Serves the guard's per-month state load and the proxy's active-hard-block lookup.
        builder.HasIndex(e => e.MonthStart);
    }

    public async Task<ICostLimitBreach> Map(
        CostLimitBreachEntity storedEntity,
        CancellationToken cancellationToken = default)
        => factory(
            costLimit: await costLimits.GetAsync(storedEntity.CostLimit, cancellationToken),
            monthStart: storedEntity.MonthStart,
            threshold: storedEntity.Threshold,
            spendEur: storedEntity.SpendEur,
            existing: storedEntity);

    public Task<CostLimitBreachEntity> Map(
        ICostLimitBreach domainEntity,
        CancellationToken cancellationToken = default)
        => new CostLimitBreachEntity
        {
            Id = domainEntity.Id,
            CostLimit = domainEntity.CostLimit.Id,
            MonthStart = domainEntity.MonthStart,
            Threshold = domainEntity.Threshold,
            SpendEur = domainEntity.SpendEur,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
