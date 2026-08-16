using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.Project;

internal class ProjectUserConfig : AbstractEntityConfiguration<ProjectUserEntity>
{
    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<ProjectUserEntity> builder)
    {
        builder.HasKey(e => new { e.ProjectId, e.UserId });

        builder
            .HasOne<ProjectEntity>()
            .WithMany(p => p.ProjectUsers)
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.UserId);
    }
}
