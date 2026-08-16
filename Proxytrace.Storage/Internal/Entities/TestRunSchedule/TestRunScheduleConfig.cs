using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Storage.Internal.Entities.TestSuite;

namespace Proxytrace.Storage.Internal.Entities.TestRunSchedule;

internal class TestRunScheduleConfig : AbstractEntityConfiguration<TestRunScheduleEntity>, IMapper<ITestRunSchedule, TestRunScheduleEntity>
{
    private readonly IRepository<ITestSuite> suites;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly ITestRunSchedule.CreateExisting factory;
    private readonly Func<StorageDbContext> contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunScheduleConfig"/> class.
    /// </summary>
    public TestRunScheduleConfig(
        IRepository<ITestSuite> suites,
        IRepository<IModelEndpoint> endpoints,
        ITestRunSchedule.CreateExisting factory,
        Func<StorageDbContext> contextFactory)
    {
        this.suites = suites;
        this.endpoints = endpoints;
        this.factory = factory;
        this.contextFactory = contextFactory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<TestRunScheduleEntity> builder)
    {
        builder
            .HasOne<TestSuiteEntity>()
            .WithMany()
            .HasForeignKey(e => e.Suite)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.IsEnabled, e.NextRunAt });
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public async Task<ITestRunSchedule> Map(TestRunScheduleEntity storedEntity, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var endpointIds = await context.Set<TestRunScheduleEndpointEntity>()
            .AsNoTracking()
            .Where(e => e.ScheduleId == storedEntity.Id)
            .Select(e => e.EndpointId)
            .ToListAsync(cancellationToken);

        var loadedEndpoints = endpointIds.Count > 0
            ? await endpoints.GetManyAsync(endpointIds, ignoreMissing: true, cancellationToken: cancellationToken)
            : [];

        return factory(
            name: storedEntity.Name,
            suite: await suites.GetAsync(storedEntity.Suite, cancellationToken),
            endpoints: loadedEndpoints,
            interval: TimeSpan.FromMinutes(storedEntity.IntervalMinutes),
            isEnabled: storedEntity.IsEnabled,
            anchorAt: storedEntity.AnchorAt,
            nextRunAt: storedEntity.NextRunAt,
            lastRunAt: storedEntity.LastRunAt,
            existing: storedEntity);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<TestRunScheduleEntity> Map(ITestRunSchedule domainEntity, CancellationToken cancellationToken = default)
        => new TestRunScheduleEntity
        {
            Id = domainEntity.Id,
            Name = domainEntity.Name,
            Suite = domainEntity.Suite.Id,
            // Minute granularity: the scheduler polls per-minute, so sub-minute intervals are not representable.
            IntervalMinutes = (int)Math.Round(domainEntity.Interval.TotalMinutes),
            IsEnabled = domainEntity.IsEnabled,
            AnchorAt = domainEntity.AnchorAt,
            NextRunAt = domainEntity.NextRunAt,
            LastRunAt = domainEntity.LastRunAt,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            ScheduleEndpoints = domainEntity.Endpoints
                .Select(e => new TestRunScheduleEndpointEntity { ScheduleId = domainEntity.Id, EndpointId = e.Id })
                .ToList()
        }.ToTaskResult();
}
