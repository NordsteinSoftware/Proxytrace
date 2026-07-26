using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Proxytrace.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.Project;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.CostLimit;

internal class CostLimitConfig
    : AbstractEntityConfiguration<CostLimitEntity>,
      IMapper<ICostLimit, CostLimitEntity>
{
    private readonly IRepository<IProject> projects;
    private readonly IRepository<IAgent> agents;
    private readonly ICostLimit.CreateExisting factory;

    public CostLimitConfig(
        IRepository<IProject> projects,
        IRepository<IAgent> agents,
        ICostLimit.CreateExisting factory)
    {
        this.projects = projects;
        this.agents = agents;
        this.factory = factory;
    }

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

        builder.Property(e => e.SoftLimitEur).HasPrecision(18, 6);
        builder.Property(e => e.HardLimitEur).HasPrecision(18, 6);

        // Two partial unique indexes rather than one composite: PostgreSQL treats NULLs as
        // distinct, so a plain unique (Project, Agent) would happily accept several project-wide
        // rows. Split by scope, each side gets a real uniqueness guarantee.
        builder
            .HasIndex(e => e.Project)
            .IsUnique()
            .HasFilter("\"Agent\" IS NULL")
            .HasDatabaseName("IX_CostLimitEntity_Project_ProjectScope");

        builder
            .HasIndex(e => new { e.Project, e.Agent })
            .IsUnique()
            .HasFilter("\"Agent\" IS NOT NULL")
            .HasDatabaseName("IX_CostLimitEntity_Project_Agent_AgentScope");

        // Serves the guard's cross-project working-set query.
        builder.HasIndex(e => e.Enabled);
    }

    public async Task<ICostLimit> Map(CostLimitEntity storedEntity, CancellationToken cancellationToken = default)
    {
        Task<IProject> projectTask = projects.GetAsync(storedEntity.Project, cancellationToken);
        Task<IAgent?> agentTask = LoadAgentAsync(storedEntity.Agent, cancellationToken);
        await Task.WhenAll(projectTask, agentTask);

        IProject project = await projectTask;
        IAgent? agent = await agentTask;

        return factory(
            project: project,
            agent: agent,
            softLimitEur: storedEntity.SoftLimitEur,
            hardLimitEur: storedEntity.HardLimitEur,
            enabled: storedEntity.Enabled,
            existing: storedEntity);
    }

    private async Task<IAgent?> LoadAgentAsync(Guid? agentId, CancellationToken cancellationToken)
        => agentId is { } id ? await agents.GetAsync(id, cancellationToken) : null;

    public Task<CostLimitEntity> Map(ICostLimit domainEntity, CancellationToken cancellationToken = default)
        => new CostLimitEntity
        {
            Id = domainEntity.Id,
            Project = domainEntity.Project.Id,
            Agent = domainEntity.Agent?.Id,
            SoftLimitEur = domainEntity.SoftLimitEur,
            HardLimitEur = domainEntity.HardLimitEur,
            Enabled = domainEntity.Enabled,
            CreatedAt = domainEntity.CreatedAt,
            UpdatedAt = domainEntity.UpdatedAt,
        }.ToTaskResult();
}
