using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Entities.TestRunSchedule;

internal class TestRunScheduleEndpointConfig : AbstractEntityConfiguration<TestRunScheduleEndpointEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<TestRunScheduleEndpointEntity> builder)
    {
        builder.HasKey(e => new { e.ScheduleId, e.EndpointId });

        builder
            .HasOne<TestRunScheduleEntity>()
            .WithMany(e => e.ScheduleEndpoints)
            .HasForeignKey(e => e.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<ModelEndpointEntity>()
            .WithMany()
            .HasForeignKey(e => e.EndpointId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.EndpointId);
    }
}
