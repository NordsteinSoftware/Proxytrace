using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Storage.Internal.Entities.TestRunSchedule;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.TestRunGroup;

internal class TestRunGroupConfig : AbstractEntityConfiguration<TestRunGroupEntity>, IMapper<ITestRunGroup, TestRunGroupEntity>
{
    private readonly IRepository<ITestSuite> suites;
    private readonly ITestRunGroup.CreateExisting factory;

    public TestRunGroupConfig(
        IRepository<ITestSuite> suites,
        ITestRunGroup.CreateExisting factory)
    {
        this.suites = suites;
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<TestRunGroupEntity> builder)
    {
        builder
            .HasOne<TestSuiteEntity>()
            .WithMany()
            .HasForeignKey(e => e.Suite)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<TestRunScheduleEntity>()
            .WithMany()
            .HasForeignKey(e => e.ScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backfill pre-existing groups (created before sampling) to a single sample per endpoint.
        builder
            .Property(e => e.SampleCount)
            .HasDefaultValue(1);

        // Serves the optimizer's restart recovery, which asks for terminal, non-system groups that
        // have not been considered yet. Leading on the marker keeps that an index range read over
        // the small unconsidered tail rather than a scan of every group ever run.
        builder.HasIndex(e => new { e.OptimizationConsideredAt, e.IsSystemRun, e.Status });
    }

    public async Task<ITestRunGroup> Map(TestRunGroupEntity stored, CancellationToken cancellationToken = default)
        => factory(
            suite: await suites.GetAsync(stored.Suite, cancellationToken),
            status: stored.Status,
            completedAt: stored.CompletedAt,
            isSystemRun: stored.IsSystemRun,
            scheduleId: stored.ScheduleId,
            sampleCount: stored.SampleCount,
            optimizationConsideredAt: stored.OptimizationConsideredAt,
            existing: stored);

    public Task<TestRunGroupEntity> Map(ITestRunGroup domain, CancellationToken cancellationToken = default)
        => new TestRunGroupEntity
        {
            Id = domain.Id,
            Suite = domain.Suite.Id,
            Status = domain.Status,
            CompletedAt = domain.CompletedAt,
            IsSystemRun = domain.IsSystemRun,
            ScheduleId = domain.ScheduleId,
            SampleCount = domain.SampleCount,
            OptimizationConsideredAt = domain.OptimizationConsideredAt,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
