using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyResult;

internal class CustomAnomalyResultConfig
    : AbstractEntityConfiguration<CustomAnomalyResultEntity>,
      IMapper<ICustomAnomalyResult, CustomAnomalyResultEntity>
{
    private readonly ICustomAnomalyResult.CreateExisting factory;

    public CustomAnomalyResultConfig(ICustomAnomalyResult.CreateExisting factory)
    {
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<CustomAnomalyResultEntity> builder)
    {
        // Results are owned attribution rows: they never outlive their detector or their call
        // (retention cleanup of old traces sweeps their results along at the database level).
        builder
            .HasOne<CustomAnomalyDetectorEntity>()
            .WithMany()
            .HasForeignKey(e => e.DetectorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<AgentCallEntity>()
            .WithMany()
            .HasForeignKey(e => e.AgentCallId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.AgentCallId);
        builder.HasIndex(e => e.DetectorId);
        builder.HasIndex(e => new { e.ProjectId, e.CreatedAt }).IsDescending(false, true);
    }

    public Task<ICustomAnomalyResult> Map(
        CustomAnomalyResultEntity storedEntity,
        CancellationToken cancellationToken = default)
        => factory(
            detectorId: storedEntity.DetectorId,
            agentCallId: storedEntity.AgentCallId,
            projectId: storedEntity.ProjectId,
            matchedTrigger: storedEntity.MatchedTrigger,
            reasoning: storedEntity.Reasoning,
            existing: storedEntity).ToTaskResult();

    public Task<CustomAnomalyResultEntity> Map(
        ICustomAnomalyResult domainEntity,
        CancellationToken cancellationToken = default)
        => new CustomAnomalyResultEntity
        {
            Id = domainEntity.Id,
            DetectorId = domainEntity.DetectorId,
            AgentCallId = domainEntity.AgentCallId,
            ProjectId = domainEntity.ProjectId,
            MatchedTrigger = domainEntity.MatchedTrigger,
            Reasoning = domainEntity.Reasoning,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
