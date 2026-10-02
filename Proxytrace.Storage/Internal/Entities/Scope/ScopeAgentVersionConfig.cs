using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.AgentVersion;

namespace Proxytrace.Storage.Internal.Entities.Scope;

internal class ScopeAgentVersionConfig : AbstractEntityConfiguration<ScopeAgentVersionEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<ScopeAgentVersionEntity> builder)
    {
        builder.HasKey(e => new { e.ScopeId, e.AgentVersionId });

        builder
            .HasOne<ScopeEntity>()
            .WithMany()
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade (not Restrict): membership is derived data that retention rebuilds from traces, so
        // it must never block deleting a version. A version that still has traces cannot be deleted
        // anyway (AgentCall → AgentVersion is Restrict).
        builder
            .HasOne<AgentVersionEntity>()
            .WithMany()
            .HasForeignKey(e => e.AgentVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.AgentVersionId);
    }
}
