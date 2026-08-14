using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Storage.Internal.Entities.Inference;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.Agent;

internal class AgentConfig : AbstractEntityConfiguration<AgentEntity>, IMapper<IAgent, AgentEntity>
{
    private readonly IAgent.CreateExisting factory;
    private readonly IModelParameters.Create modelParametersFactory;
    private readonly ISerializer serializer;
    private readonly IRepository<IProject> projects;
    private readonly IRepository<IModelEndpoint> endpoints;
    private readonly Lazy<IRepository<IAgentVersion>> versions;

    public AgentConfig(
        IAgent.CreateExisting factory,
        IModelParameters.Create modelParametersFactory,
        ISerializer serializer,
        IRepository<IProject> projects,
        IRepository<IModelEndpoint> endpoints,
        Lazy<IRepository<IAgentVersion>> versions)
    {
        this.factory = factory;
        this.modelParametersFactory = modelParametersFactory;
        this.serializer = serializer;
        this.projects = projects;
        this.endpoints = endpoints;
        this.versions = versions;
    }

    public override void Configure(EntityTypeBuilder<AgentEntity> builder)
    {
        builder.HasIndex(e => e.IsSystemAgent);
        builder.HasIndex(e => new { e.Project, e.Name });
        // Supports the project-scoped list query, which excludes archived rows.
        builder.HasIndex(e => new { e.Project, e.IsArchived });
        builder.Property(e => e.Name).HasMaxLength(200);

        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.Project)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne<ModelEndpointEntity>()
            .WithMany()
            .HasForeignKey(e => e.Endpoint)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .Property(e => e.ModelParameters)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<ModelParametersData>(v) ?? ModelParametersData.Empty
            );
    }

    public async Task<IAgent> Map(AgentEntity stored, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetAsync(stored.Project, cancellationToken);
        var endpoint = await endpoints.GetAsync(stored.Endpoint, cancellationToken);
        var modelParameters = ToDomain(stored.ModelParameters, modelParametersFactory);

        IAgentVersion currentVersion = await versions.Value.GetAsync(stored.CurrentVersionId, cancellationToken);

        return factory(
            name: stored.Name,
            project: project,
            endpoint: endpoint,
            isSystemAgent: stored.IsSystemAgent,
            modelParameters: modelParameters,
            currentVersion: currentVersion,
            existing: stored);
    }

    public Task<AgentEntity> Map(IAgent domain, CancellationToken cancellationToken = default)
        => new AgentEntity
        {
            Id = domain.Id,
            Name = domain.Name,
            Project = domain.Project.Id,
            ModelParameters = ToData(domain.ModelParameters),
            Endpoint = domain.Endpoint.Id,
            IsSystemAgent = domain.IsSystemAgent,
            IsArchived = domain.IsArchived,
            CurrentVersionId = domain.CurrentVersion.Id,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        }.ToTaskResult();

    internal static ModelParametersData ToData(IModelParameters domain)
        => new(
            domain.Temperature,
            domain.TopP,
            domain.ReasoningEffort,
            domain.FrequencyPenalty,
            domain.PresencePenalty,
            domain.MaxTokens,
            domain.Seed,
            domain.Stop,
            domain.N);

    internal static IModelParameters ToDomain(ModelParametersData data, IModelParameters.Create factory)
        => factory(
            temperature: data.Temperature,
            topP: data.TopP,
            reasoningEffort: data.ReasoningEffort,
            frequencyPenalty: data.FrequencyPenalty,
            presencePenalty: data.PresencePenalty,
            maxTokens: data.MaxTokens,
            seed: data.Seed,
            stop: data.Stop,
            n: data.N);
}
