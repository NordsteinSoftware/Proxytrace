using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

internal class CustomAnomalyDetectorConfig
    : AbstractEntityConfiguration<CustomAnomalyDetectorEntity>,
      IMapper<ICustomAnomalyDetector, CustomAnomalyDetectorEntity>
{
    private readonly IRepository<IAgent> agents;
    private readonly ICustomAnomalyDetector.CreateExisting factory;
    private readonly ISerializer serializer;
    private readonly Func<StorageDbContext> contextFactory;

    public CustomAnomalyDetectorConfig(
        IRepository<IAgent> agents,
        ICustomAnomalyDetector.CreateExisting factory,
        ISerializer serializer,
        Func<StorageDbContext> contextFactory)
    {
        this.agents = agents;
        this.factory = factory;
        this.serializer = serializer;
        this.contextFactory = contextFactory;
    }

    public override void Configure(EntityTypeBuilder<CustomAnomalyDetectorEntity> builder)
    {
        // The hidden system agent owns the detector in the delete graph: removing the agent (e.g.
        // the project-delete system-agent sweep) takes the detector — and, transitively, its
        // results — with it.
        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.Agent)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves the project-scoped list and the review pipeline's enabled-only lookup.
        builder.HasIndex(e => new { e.Project, e.IsEnabled });

        // SQL default backfills pre-existing detectors to non-blocking.
        builder.Property(e => e.BlockUpstream).HasDefaultValue(false);
    }

    public async Task<ICustomAnomalyDetector> Map(
        CustomAnomalyDetectorEntity storedEntity,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var scopedAgentIds = await context.Set<CustomAnomalyDetectorAgentEntity>()
            .AsNoTracking()
            .Where(e => e.DetectorId == storedEntity.Id)
            .Select(e => e.AgentId)
            .ToListAsync(cancellationToken);

        var scopedAgents = scopedAgentIds.Count > 0
            ? await agents.GetManyAsync(scopedAgentIds, cancellationToken, ignoreMissing: true)
            : [];

        return factory(
            name: storedEntity.Name,
            agent: await agents.GetAsync(storedEntity.Agent, cancellationToken),
            triggers: serializer.DeserializeRequired<List<AnomalyTrigger>>(storedEntity.Triggers),
            allAgents: storedEntity.AllAgents,
            scopedAgents: scopedAgents,
            isEnabled: storedEntity.IsEnabled,
            blockUpstream: storedEntity.BlockUpstream,
            existing: storedEntity);
    }

    public Task<CustomAnomalyDetectorEntity> Map(
        ICustomAnomalyDetector domainEntity,
        CancellationToken cancellationToken = default)
        => new CustomAnomalyDetectorEntity
        {
            Id = domainEntity.Id,
            Name = domainEntity.Name,
            Agent = domainEntity.Agent.Id,
            Project = domainEntity.Project.Id,
            Triggers = serializer.Serialize(domainEntity.Triggers.ToList()),
            AllAgents = domainEntity.AllAgents,
            IsEnabled = domainEntity.IsEnabled,
            BlockUpstream = domainEntity.BlockUpstream,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
            ScopedAgents = domainEntity.ScopedAgents
                .Select(a => new CustomAnomalyDetectorAgentEntity { DetectorId = domainEntity.Id, AgentId = a.Id })
                .ToList(),
        }.ToTaskResult();
}
