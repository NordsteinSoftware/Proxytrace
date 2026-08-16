using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Storage.Internal.Entities.AuditLog;

internal class AuditLogEntryConfig
    : AbstractEntityConfiguration<AuditLogEntryEntity>,
      IMapper<IAuditLogEntry, AuditLogEntryEntity>
{
    private readonly IAuditLogEntry.CreateExisting factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogEntryConfig"/> class.
    /// </summary>
    public AuditLogEntryConfig(IAuditLogEntry.CreateExisting factory)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<AuditLogEntryEntity> builder)
    {
        // Audit entries are denormalized, immutable snapshots. ActorUserId / ActorApiKeyId /
        // ProjectId / TargetId are deliberately plain Guid columns with NO foreign keys, so a
        // recorded action survives deletion of the user / key / project / target it refers to
        // (e.g. the "project deleted" row must outlive the project). Do NOT add HasOne/HasForeignKey.
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.Action);
        builder.HasIndex(e => e.ProjectId);
        builder.Property(e => e.TargetType).HasMaxLength(128);
        builder.Property(e => e.TargetLabel).HasMaxLength(512);
        builder.Property(e => e.ActorEmail).HasMaxLength(320);
        // Details is intentionally unbounded (Postgres text) — pre-serialized JSON.
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<IAuditLogEntry> Map(AuditLogEntryEntity stored, CancellationToken cancellationToken = default)
        => factory(
            stored.Action,
            stored.ActorType,
            stored.ActorUserId,
            stored.ActorEmail,
            stored.ActorApiKeyId,
            stored.ProjectId,
            stored.TargetType,
            stored.TargetId,
            stored.TargetLabel,
            stored.Details,
            stored.Outcome,
            stored).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<AuditLogEntryEntity> Map(IAuditLogEntry domain, CancellationToken cancellationToken = default)
        => new AuditLogEntryEntity
        {
            Id = domain.Id,
            Action = domain.Action,
            ActorType = domain.ActorType,
            ActorUserId = domain.ActorUserId,
            ActorEmail = domain.ActorEmail,
            ActorApiKeyId = domain.ActorApiKeyId,
            ProjectId = domain.ProjectId,
            TargetType = domain.TargetType,
            TargetId = domain.TargetId,
            TargetLabel = domain.TargetLabel,
            Details = domain.Details,
            Outcome = domain.Outcome,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
