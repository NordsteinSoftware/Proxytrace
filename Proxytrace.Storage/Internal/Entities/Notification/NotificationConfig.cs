using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Notification;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.Notification;

internal class NotificationConfig :
    AbstractEntityConfiguration<NotificationEntity>,
    IMapper<INotification, NotificationEntity>
{
    private readonly INotification.CreateExisting factory;

    public NotificationConfig(INotification.CreateExisting factory)
    {
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        // Cascade with the owning project. ProjectId is nullable, so EF treats the relationship as
        // optional and global (null-project) notifications are unaffected.
        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK on TargetId — it is a soft, polymorphic reference (see INotification.TargetId).

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ProjectId);
        builder.HasIndex(e => new { e.ProjectId, e.Status });
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.TargetKind, e.TargetId });
    }

    public Task<INotification> Map(NotificationEntity stored, CancellationToken cancellationToken = default)
        => factory(
            stored.Kind,
            stored.Severity,
            stored.Title,
            stored.Message,
            stored.Status,
            stored.ProjectId,
            stored.TargetKind,
            stored.TargetId,
            stored).ToTaskResult();

    public Task<NotificationEntity> Map(INotification domain, CancellationToken cancellationToken = default)
        => new NotificationEntity
        {
            Id = domain.Id,
            Kind = domain.Kind,
            Severity = domain.Severity,
            Title = domain.Title,
            Message = domain.Message,
            Status = domain.Status,
            ProjectId = domain.ProjectId,
            TargetKind = domain.TargetKind,
            TargetId = domain.TargetId,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
