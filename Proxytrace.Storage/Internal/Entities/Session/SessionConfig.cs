using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Common.Async;
using Proxytrace.Domain.Session;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.Session;

internal class SessionConfig
    : AbstractEntityConfiguration<SessionEntity>,
      IMapper<ISession, SessionEntity>
{
    private readonly ISession.CreateExisting factory;

    public SessionConfig(ISession.CreateExisting factory)
    {
        this.factory = factory;
    }

    public override void Configure(EntityTypeBuilder<SessionEntity> builder)
    {
        // Sessions are project-owned debugging groupings; they go away with their project. The
        // traces that point at a session hold a plain FK-free Guid (like ConversationId), so
        // deleting a session never touches the irreplaceable traces.
        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.ExternalKey).HasMaxLength(ISession.MaxExternalKeyLength);
        builder.HasIndex(e => new { e.ProjectId, e.ExternalKey }).IsUnique();
        builder.HasIndex(e => new { e.ProjectId, e.LastActivityAt }).IsDescending(false, true);
    }

    public Task<ISession> Map(SessionEntity storedEntity, CancellationToken cancellationToken = default)
        => factory(
            externalKey: storedEntity.ExternalKey,
            projectId: storedEntity.ProjectId,
            lastActivityAt: storedEntity.LastActivityAt,
            traceCount: storedEntity.TraceCount,
            totalTokens: storedEntity.TotalTokens,
            existing: storedEntity).ToTaskResult();

    public Task<SessionEntity> Map(ISession domainEntity, CancellationToken cancellationToken = default)
        => new SessionEntity
        {
            Id = domainEntity.Id,
            ExternalKey = domainEntity.ExternalKey,
            ProjectId = domainEntity.ProjectId,
            LastActivityAt = domainEntity.LastActivityAt,
            TraceCount = domainEntity.TraceCount,
            TotalTokens = domainEntity.TotalTokens,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
