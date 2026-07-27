using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Storage.Internal.Entities.ModelProvider;
using Proxytrace.Storage.Internal.Entities.Project;
using Proxytrace.Storage.Internal.Entities.User;

namespace Proxytrace.Storage.Internal.Entities.ApiKey;

internal class ApiKeyConfig : AbstractEntityConfiguration<ApiKeyEntity>, IMapper<IApiKey, ApiKeyEntity>
{
    private readonly IApiKey.CreateExisting factory;
    private readonly IRepository<IProject> projects;
    private readonly IRepository<IModelProvider> providers;
    private readonly IRepository<IUser> users;

    public ApiKeyConfig(
        IApiKey.CreateExisting factory,
        IRepository<IProject> projects,
        IRepository<IModelProvider> providers,
        IRepository<IUser> users)
    {
        this.factory = factory;
        this.projects = projects;
        this.providers = providers;
        this.users = users;
    }

    public override void Configure(EntityTypeBuilder<ApiKeyEntity> builder)
    {
        builder.HasIndex(e => e.KeyHash).IsUnique();
        builder.Property(e => e.Name).HasMaxLength(256).IsRequired();
        builder.Property(e => e.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.KeyPrefix).HasMaxLength(32);

        // SQL default backfills pre-scopes keys to Ingestion-only, so existing ingestion keys do not
        // silently gain MCP capabilities (least privilege). New keys always set Scopes explicitly.
        builder.Property(e => e.Scopes).HasDefaultValue(Domain.ApiKey.ApiKeyScopes.Ingestion);

        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.Project)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade. Only the key's hash is stored, so a deleted key is unrecoverable —
        // it cannot be re-issued, only replaced, and every integration configured with it breaks at
        // once with no diagnostic beyond a 401. Cascading meant hard-deleting a provider silently
        // destroyed every key issued for it. The provider repository is already archive-only
        // (SupportsHardDelete => false), so this is the database-level backstop for the paths the
        // repository never sees (raw SQL, EF bulk deletes) — the same pairing ModelEndpoint uses.
        builder
            .HasOne<ModelProviderEntity>()
            .WithMany()
            .HasForeignKey(e => e.Provider)
            .OnDelete(DeleteBehavior.Restrict);

        // Also Restrict. A key acts as its owner, so it genuinely must not outlive them — but
        // cascading turned "offboard this admin" into "silently revoke every integration they
        // happened to mint", discovered only when production traffic started failing.
        // UserAdministrationService now refuses the delete with a 409 naming the keys, so an
        // operator deletes or reassigns them deliberately instead of finding out afterwards.
        builder
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(e => e.Owner)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public async Task<IApiKey> Map(ApiKeyEntity stored, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetAsync(stored.Project, cancellationToken);
        var provider = await providers.GetAsync(stored.Provider, cancellationToken);
        var owner = await users.GetAsync(stored.Owner, cancellationToken);
        return factory(stored.Name, stored.KeyHash, stored.KeyPrefix ?? string.Empty, project, provider, stored.Scopes, owner, stored);
    }

    public Task<ApiKeyEntity> Map(IApiKey domain, CancellationToken cancellationToken = default)
        => new ApiKeyEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            KeyHash = domain.KeyHash,
            KeyPrefix = domain.KeyPrefix,
            Project = domain.Project.Id,
            Provider = domain.Provider.Id,
            Scopes = domain.Scopes,
            Owner = domain.Owner.Id,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();
}
