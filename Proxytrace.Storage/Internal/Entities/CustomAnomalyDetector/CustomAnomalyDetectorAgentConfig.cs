using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.Agent;

namespace Proxytrace.Storage.Internal.Entities.CustomAnomalyDetector;

internal class CustomAnomalyDetectorAgentConfig : AbstractEntityConfiguration<CustomAnomalyDetectorAgentEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<CustomAnomalyDetectorAgentEntity> builder)
    {
        builder.HasKey(e => new { e.DetectorId, e.AgentId });

        builder
            .HasOne<CustomAnomalyDetectorEntity>()
            .WithMany(e => e.ScopedAgents)
            .HasForeignKey(e => e.DetectorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade (not Restrict): a scoped agent's deletion silently detaches it from the
        // detector's scope — a scope entry must never block deleting an agent.
        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.AgentId);
    }
}
