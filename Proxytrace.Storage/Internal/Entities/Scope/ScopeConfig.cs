using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain.Scope;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.Scope;

internal class ScopeConfig
    : AbstractEntityConfiguration<ScopeEntity>,
      IMapper<IScope, ScopeEntity>
{
    private readonly IScope.CreateExisting factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeConfig"/> class.
    /// </summary>
    public ScopeConfig(IScope.CreateExisting factory)
    {
        this.factory = factory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<ScopeEntity> builder)
    {
        // Project-owned, like sessions. Traces reference a scope through a plain FK-free Guid, so
        // removing a scope never touches the irreplaceable traces.
        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.ExternalKey).HasMaxLength(ScopeKey.MaxLength);
        builder.Property(e => e.DisplayName).HasMaxLength(IScope.MaxDisplayNameLength);
        builder.Property(e => e.Description).HasMaxLength(IScope.MaxDescriptionLength);
        builder.HasIndex(e => new { e.ProjectId, e.ExternalKey }).IsUnique();
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<IScope> Map(ScopeEntity storedEntity, CancellationToken cancellationToken = default)
        => factory(
            externalKey: storedEntity.ExternalKey,
            projectId: storedEntity.ProjectId,
            displayName: storedEntity.DisplayName,
            description: storedEntity.Description,
            existing: storedEntity).ToTaskResult();

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<ScopeEntity> Map(IScope domainEntity, CancellationToken cancellationToken = default)
        => new ScopeEntity
        {
            Id = domainEntity.Id,
            ExternalKey = domainEntity.ExternalKey,
            ProjectId = domainEntity.ProjectId,
            DisplayName = domainEntity.DisplayName,
            Description = domainEntity.Description,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
