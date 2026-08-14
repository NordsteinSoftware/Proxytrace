using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.Project;

namespace Proxytrace.Storage.Internal.Entities.AgentVersion;

internal class AgentVersionConfig : AbstractEntityConfiguration<AgentVersionEntity>, IMapper<IAgentVersion, AgentVersionEntity>
{
    private readonly IAgentVersion.CreateExisting factory;
    private readonly IPromptTemplate.Create promptTemplateFactory;
    private readonly ISerializer serializer;
    private readonly IAgentVersionFingerprinter fingerprinter;

    public AgentVersionConfig(
        IAgentVersion.CreateExisting factory,
        IPromptTemplate.Create promptTemplateFactory,
        ISerializer serializer,
        IAgentVersionFingerprinter fingerprinter)
    {
        this.factory = factory;
        this.promptTemplateFactory = promptTemplateFactory;
        this.serializer = serializer;
        this.fingerprinter = fingerprinter;
    }

    public override void Configure(EntityTypeBuilder<AgentVersionEntity> builder)
    {
        builder.HasIndex(e => new { e.AgentId, e.VersionNumber }).IsUnique();
        builder.HasIndex(e => e.AgentId);
        builder.HasIndex(e => new { e.Project, e.Fingerprint }).IsUnique();
        builder.HasIndex(e => new { e.Project, e.LooseFingerprint });
        builder.Property(e => e.Fingerprint).HasMaxLength(64);
        builder.Property(e => e.LooseFingerprint).HasMaxLength(64);

        builder
            .HasOne<AgentEntity>()
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<ProjectEntity>()
            .WithMany()
            .HasForeignKey(e => e.Project)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .Property(e => e.SystemPrompt)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<SystemPromptData>(v) ?? new SystemPromptData(string.Empty, string.Empty));

        builder
            .Property(e => e.Tools)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<IReadOnlyList<ToolSpecification>>(v) ?? Array.Empty<ToolSpecification>());
    }

    public Task<IAgentVersion> Map(AgentVersionEntity stored, CancellationToken cancellationToken = default)
    {
        var prompt = promptTemplateFactory(stored.SystemPrompt.Name, stored.SystemPrompt.Template);
        return Task.FromResult(factory(
            projectId: stored.Project,
            agentId: stored.AgentId,
            versionNumber: stored.VersionNumber,
            systemPrompt: prompt,
            tools: stored.Tools,
            existing: stored));
    }

    public Task<AgentVersionEntity> Map(IAgentVersion domain, CancellationToken cancellationToken = default)
        => Task.FromResult(new AgentVersionEntity
        {
            Id = domain.Id,
            AgentId = domain.AgentId,
            Project = domain.ProjectId,
            VersionNumber = domain.VersionNumber,
            SystemPrompt = new SystemPromptData(domain.SystemPrompt.Name, domain.SystemPrompt.Template),
            Tools = domain.Tools,
            Fingerprint = fingerprinter.Strict(domain.SystemPrompt, domain.Tools),
            LooseFingerprint = fingerprinter.Loose(domain.SystemPrompt, domain.Tools),
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
        });
}
