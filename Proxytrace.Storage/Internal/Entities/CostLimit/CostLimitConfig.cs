using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Project;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.ApiKey;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

internal class CostLimitConfig
    : AbstractEntityConfiguration<CostLimitEntity>,
      IMapper<ICostLimit, CostLimitEntity>
{
    private readonly IRepository<IProject> projects;
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<IApiKey> apiKeys;
    private readonly ICostLimit.CreateExisting factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CostLimitConfig"/> class.
    /// </summary>
    public CostLimitConfig(
        IRepository<IProject> projects,
        IRepository<IAgent> agents,
        IRepository<IApiKey> apiKeys,
        ICostLimit.CreateExisting factory)
    {
        this.projects = projects;
        this.agents = agents;
        this.apiKeys = apiKeys;
        this.factory = factory;
    }

    /// <summary>
    /// Configures the application request pipeline.
    /// </summary>
    public override void Configure(EntityTypeBuilder<CostLimitEntity> builder)
    {
        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.Project)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.Agent)
            .OnDelete(DeleteBehavior.Cascade);

        // Revoking a key takes its budget with it. The key's *traces* keep their ApiKeyId
        // attribution (that column is deliberately FK-free), so history survives — only the
        // now-meaningless limit configuration goes.
        builder
            .HasOne<ApiKeyEntity>()
            .WithMany()
            .HasForeignKey(e => e.ApiKey)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.SoftLimitEur).HasPrecision(18, 6);
        builder.Property(e => e.HardLimitEur).HasPrecision(18, 6);

        // Three partial unique indexes rather than one composite: PostgreSQL treats NULLs as
        // distinct, so a plain unique (Project, Agent, ApiKey) would happily accept several
        // project-wide rows. Split by scope, each side gets a real uniqueness guarantee.
        builder
            .HasIndex(e => e.Project)
            .IsUnique()
            .HasFilter("\"Agent\" IS NULL AND \"ApiKey\" IS NULL")
            .HasDatabaseName("IX_CostLimitEntity_Project_ProjectScope");

        builder
            .HasIndex(e => new { e.Project, e.Agent })
            .IsUnique()
            .HasFilter("\"Agent\" IS NOT NULL")
            .HasDatabaseName("IX_CostLimitEntity_Project_Agent_AgentScope");

        builder
            .HasIndex(e => new { e.Project, e.ApiKey })
            .IsUnique()
            .HasFilter("\"ApiKey\" IS NOT NULL")
            .HasDatabaseName("IX_CostLimitEntity_Project_ApiKey_ApiKeyScope");

        // Serves the guard's cross-project working-set query.
        builder.HasIndex(e => e.Enabled);
    }

    /// <summary>
    /// Maps.
    /// </summary>
    public async Task<ICostLimit> Map(CostLimitEntity storedEntity, CancellationToken cancellationToken = default)
    {
        // Sequential, NOT Task.WhenAll: inside a transaction every repository shares one
        // StorageDbContext (Func<StorageDbContext> returns ambient.Context), and two concurrent
        // operations on it throw "A second operation was started on this context instance". A
        // mapper cannot know whether its caller opened a transaction, so it must never parallelize.
        // All three are indexed point lookups, so there is nothing to win here anyway.
        IProject project = await projects.GetAsync(storedEntity.Project, cancellationToken);
        IAgent? agent = await LoadAgentAsync(storedEntity.Agent, cancellationToken);
        IApiKey? apiKey = await LoadApiKeyAsync(storedEntity.ApiKey, cancellationToken);

        return factory(
            project: project,
            agent: agent,
            apiKey: apiKey,
            softLimitEur: storedEntity.SoftLimitEur,
            hardLimitEur: storedEntity.HardLimitEur,
            enabled: storedEntity.Enabled,
            existing: storedEntity);
    }

    private async Task<IAgent?> LoadAgentAsync(Guid? agentId, CancellationToken cancellationToken)
        => agentId is { } id ? await agents.GetAsync(id, cancellationToken) : null;

    private async Task<IApiKey?> LoadApiKeyAsync(Guid? apiKeyId, CancellationToken cancellationToken)
        => apiKeyId is { } id ? await apiKeys.GetAsync(id, cancellationToken) : null;

    /// <summary>
    /// Maps.
    /// </summary>
    public Task<CostLimitEntity> Map(ICostLimit domainEntity, CancellationToken cancellationToken = default)
        => new CostLimitEntity
        {
            Id = domainEntity.Id,
            Project = domainEntity.Project.Id,
            Agent = domainEntity.Agent?.Id,
            ApiKey = domainEntity.ApiKey?.Id,
            SoftLimitEur = domainEntity.SoftLimitEur,
            HardLimitEur = domainEntity.HardLimitEur,
            Enabled = domainEntity.Enabled,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
