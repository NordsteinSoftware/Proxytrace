using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

internal class AgentCallToolConfig : AbstractEntityConfiguration<AgentCallToolEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<AgentCallToolEntity> builder)
    {
        builder.Property(t => t.ToolName).HasMaxLength(256);

        builder
            .HasOne<AgentCallEntity>()
            .WithMany(e => e.Tools)
            .HasForeignKey(t => t.AgentCallId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves the tool-name picker's project-wide DISTINCT query (index-only scan).
        builder.HasIndex(t => new { t.ProjectId, t.ToolName });

        // Serves the tool-name picker's agent-scoped DISTINCT query (agent filter active) — keeps it
        // single-table + index-only, mirroring the project-wide index above.
        builder.HasIndex(t => new { t.ProjectId, t.AgentId, t.ToolName });

        // Serves the EXISTS filter semi-join (AgentCallId == e.Id && ToolName == filter.ToolName).
        builder.HasIndex(t => new { t.ToolName, t.AgentCallId });
    }
}
