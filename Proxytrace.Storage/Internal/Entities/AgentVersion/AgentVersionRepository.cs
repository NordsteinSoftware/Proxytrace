using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Storage.Internal.Entities.AgentVersion;

[UsedImplicitly]
internal class AgentVersionRepository : AbstractRepository<IAgentVersion, AgentVersionEntity>, IAgentVersionRepository
{
    private readonly IAgentVersionFingerprinter fingerprinter;

    public AgentVersionRepository(
        IMapper<IAgentVersion, AgentVersionEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        IAgentVersionFingerprinter fingerprinter,
        IEntityCache<IAgentVersion> cache,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
        this.fingerprinter = fingerprinter;
    }

    public async Task<IAgentVersion?> FindByStrictFingerprintAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = fingerprinter.Strict(systemPrompt, tools);
        var existing = await contextFactory()
            .Set<AgentVersionEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Project == project.Id && e.Fingerprint == fingerprint, cancellationToken);
        return existing is null ? null : await mapper.Map(existing, cancellationToken);
    }

    public async Task<IReadOnlyList<IAgentVersion>> GetByLooseFingerprintAsync(
        IProject project,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default)
    {
        var loose = fingerprinter.Loose(systemPrompt, tools);
        var stored = await contextFactory()
            .Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(e => e.Project == project.Id && e.LooseFingerprint == loose)
            .ToListAsync(cancellationToken);
        return await Map(stored, cancellationToken);
    }

    public async Task<IReadOnlyList<IAgentVersion>> GetByAgentAsync(
        IAgent agent,
        CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(e => e.AgentId == agent.Id)
            .OrderBy(e => e.VersionNumber)
            .ToListAsync(cancellationToken);
        return await Map(stored, cancellationToken);
    }

    public string GetStrictFingerprint(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools)
        => fingerprinter.Strict(systemPrompt, tools);
}
