using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Prompt;

namespace Proxytrace.Domain.AgentVersion.Internal;

internal class AgentVersionGenerator : DomainEntityGenerator<IAgentVersion>, IAgentVersionGenerator
{
    private readonly IAgentVersion.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IAgentVersionRepository versionRepository;
    private readonly IDomainObjectGenerator<IPromptTemplate> promptTemplateGenerator;

    public AgentVersionGenerator(
        IAgentVersion.CreateNew factory,
        IRepository<IAgentVersion> repository,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IAgentVersionRepository versionRepository,
        IDomainObjectGenerator<IPromptTemplate> promptTemplateGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.versionRepository = versionRepository;
        this.promptTemplateGenerator = promptTemplateGenerator;
    }

    public override async Task<IAgentVersion> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var existing = await versionRepository.GetByAgentAsync(agent, cancellationToken);
        int next = existing.Count == 0 ? 1 : existing.Max(v => v.VersionNumber) + 1;
        var prompt = await promptTemplateGenerator.CreateAsync(cancellationToken);
        return factory(
            projectId: agent.Project.Id,
            agentId: agent.Id,
            versionNumber: next,
            systemPrompt: prompt,
            tools: []);
    }

    /// <summary>
    /// Every <see cref="IAgent"/> already owns its v1 (created atomically with the agent). To
    /// produce a unique <see cref="IAgentVersion"/> per call, create a fresh agent and return
    /// its auto-attached v1 — avoids tripping the (AgentId, VersionNumber) unique index and
    /// keeps callers (e.g. generic <c>EntityTestCases</c>) seeing distinct entities each call.
    /// </summary>
    public override async Task<IAgentVersion> CreateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.CreateAsync(cancellationToken);
        return agent.CurrentVersion;
    }
}
