using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.TestRun;

namespace Proxytrace.Storage.Internal.Entities.Statistics;

internal class TestRunStatsConfig : AbstractEntityConfiguration<TestRunStatsEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<TestRunStatsEntity> builder)
    {
        builder
            .HasOne<TestRunEntity>()
            .WithOne()
            .HasForeignKey<TestRunStatsEntity>(e => e.TestRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.TestRunId).IsUnique();
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => e.EndpointId);
        builder.HasIndex(e => e.GroupId);
        builder.HasIndex(e => e.SuiteId);
        builder.HasIndex(e => e.RunCompletedAt);
    }
}
