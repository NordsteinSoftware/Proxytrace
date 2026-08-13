using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Domain.AgentVersion.Internal;

internal record AgentVersion : DomainEntity<IAgentVersion>, IAgentVersion
{
    private readonly IRepository<IAgent> agentRepository;
    private readonly Lazy<IAgentVersionRepository> versionRepository;
    private readonly IAsyncLock locker;

    public Guid ProjectId { get; private init; }
    public Guid AgentId { get; private init; }
    public int VersionNumber { get; private init; }
    public IPromptTemplate SystemPrompt { get; }
    public IReadOnlyList<ToolSpecification> Tools { get; }

    public AgentVersion(
        Guid projectId,
        Guid agentId,
        int versionNumber,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IRepository<IAgentVersion> repository,
        IRepository<IAgent> agentRepository,
        Lazy<IAgentVersionRepository> versionRepository,
        IAsyncLock locker) : base(repository)
    {
        this.agentRepository = agentRepository;
        this.versionRepository = versionRepository;
        this.locker = locker;
        ProjectId = projectId;
        AgentId = agentId;
        VersionNumber = versionNumber;
        SystemPrompt = systemPrompt;
        Tools = tools;
    }

    public AgentVersion(
        Guid projectId,
        Guid agentId,
        int versionNumber,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IDomainEntityData existing,
        IRepository<IAgentVersion> repository,
        IRepository<IAgent> agentRepository,
        Lazy<IAgentVersionRepository> versionRepository,
        IAsyncLock locker) : base(existing, repository)
    {
        this.agentRepository = agentRepository;
        this.versionRepository = versionRepository;
        this.locker = locker;
        ProjectId = projectId;
        AgentId = agentId;
        VersionNumber = versionNumber;
        SystemPrompt = systemPrompt;
        Tools = tools;
    }

    public Task<IAgent> GetAgentAsync(CancellationToken cancellationToken = default)
        => agentRepository.GetAsync(AgentId, cancellationToken);

    public async Task<IAgentVersion> MoveToAgentAsync(IAgent targetAgent, CancellationToken cancellationToken = default)
    {
        // Process-local lock — same caveat as Agent.CreateNewVersionAsync: in multi-replica
        // deployments, the unique (AgentId, VersionNumber) index is the actual safety net.
        using IDisposable lockObj = await locker.LockAsync($"agent-versions:{targetAgent.Id}", cancellationToken);
        var existing = await versionRepository.Value.GetByAgentAsync(targetAgent, cancellationToken);
        int next = existing.Count == 0
            ? 1
            : existing.Max(v => v.VersionNumber) + 1;
        // ProjectId is denormalised at creation and never changes on re-parent (see IAgentVersion):
        // moves are constrained to the same project, so it is intentionally not reassigned here.
        return await ApplyAsync(
            this with { AgentId = targetAgent.Id, VersionNumber = next },
            cancellationToken);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotDefault(AgentId, nameof(AgentId));
        yield return Validation.NotDefault(ProjectId, nameof(ProjectId));
        yield return Validation.NotNull(SystemPrompt, nameof(SystemPrompt));

        if (VersionNumber < 1)
        {
            yield return new ValidationResult(
                $"{nameof(VersionNumber)} must be >= 1", [nameof(VersionNumber)]);
        }

        foreach (var result in SystemPrompt.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var tool in Tools)
        {
            foreach (var result in tool.Validate(validationContext))
            {
                yield return result;
            }
        }
    }
}
