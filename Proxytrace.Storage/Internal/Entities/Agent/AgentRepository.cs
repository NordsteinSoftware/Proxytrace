using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.AI.Prompts;
using Proxytrace.Domain.Prompt;
using Nordstein.Core.AI.Tools;
using Proxytrace.Storage.Internal.Entities.AgentVersion;

namespace Proxytrace.Storage.Internal.Entities.Agent;

[UsedImplicitly]
internal class AgentRepository : ArchivableRepository<IAgent, AgentEntity>, IAgentRepository
{
    private readonly IAgent.CreateNew createNew;
    private readonly Lazy<IMapper<IAgentVersion, AgentVersionEntity>> versionMapper;
    private readonly IPromptTemplate.Create promptTemplateFactory;
    private readonly IModelParameters.Create modelParametersFactory;
    private readonly Lazy<IAgentNameGenerator> nameGenerator;
    private readonly IAsyncLock locker;
    private readonly IAgentVersionRepository versionQueries;
    private readonly IAgentVersionFingerprinter fingerprinter;
    private readonly IEntityCache<IAgentVersion>? versionCache;

    /// <summary>
    /// Initializes the repository with all collaborators required for agent persistence: the base
    /// mapper/context/event infrastructure, fingerprinting services for deduplication, a name
    /// generator for auto-named agents, an async lock to serialize concurrent GetOrCreate races, and
    /// optional caches for agents and versions.
    /// </summary>
    public AgentRepository(
        IMapper<IAgent, AgentEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        IAgent.CreateNew createNew,
        Lazy<IMapper<IAgentVersion, AgentVersionEntity>> versionMapper,
        IPromptTemplate.Create promptTemplateFactory,
        IModelParameters.Create modelParametersFactory,
        Lazy<IAgentNameGenerator> nameGenerator,
        IAsyncLock locker,
        IAgentVersionRepository versionQueries,
        IAgentVersionFingerprinter fingerprinter,
        IEntityCache<IAgent> cache,
        AmbientDbContext ambient,
        IEntityCache<IAgentVersion>? versionCache = null) : base(mapper, contextFactory, transaction, entityEvents, ambient, cache)
    {
        this.createNew = createNew;
        this.versionMapper = versionMapper;
        this.promptTemplateFactory = promptTemplateFactory;
        this.modelParametersFactory = modelParametersFactory;
        this.nameGenerator = nameGenerator;
        this.locker = locker;
        this.versionQueries = versionQueries;
        this.fingerprinter = fingerprinter;
        this.versionCache = versionCache;
    }

    /// <summary>
    /// Persists an agent update if the agent already exists, or creates the agent together with its
    /// initial version if it does not. Delegates to <c>PersistWithInitialVersionAsync</c> for new agents
    /// to satisfy the storage invariant that every agent row has a corresponding version row.
    /// </summary>
    public override async Task<IAgent> UpsertAsync(IAgent entity, CancellationToken cancellationToken = default)
    {
        if (await this.ContainsAsync(entity.Id, cancellationToken))
        {
            return await UpdateAsync(entity, cancellationToken);
        }
        return await PersistWithInitialVersionAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Returns the agent whose current version matches the given system prompt and tool set within the project,
    /// creating a new agent and version if no match exists. Uses an in-process async lock keyed by strict
    /// fingerprint to serialize concurrent creation races; a DB-level unique constraint on the fingerprint
    /// handles the remaining multi-instance race.
    /// </summary>
    public async Task<IAgent> GetOrCreateAsync(
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IProject project,
        IModelEndpoint endpoint,
        string? name = null,
        bool isSystemAgent = false,
        IModelParameters? modelParameters = null,
        bool skipStrictPreCheck = false,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = GetAgentFingerprint(systemPrompt, tools);
        using IDisposable lockObj = await locker.LockAsync(fingerprint, cancellationToken);

        if (!skipStrictPreCheck)
        {
            var existingVersion = await versionQueries.FindByStrictFingerprintAsync(project, systemPrompt, tools, cancellationToken);
            if (existingVersion is not null)
            {
                return await existingVersion.GetAgentAsync(cancellationToken);
            }
        }

        name ??= await nameGenerator.Value.GenerateNameAsync(systemPrompt, project, cancellationToken);
        var namedPrompt = promptTemplateFactory(name, systemPrompt.Template);

        try
        {
            return await CreateWithInitialVersionAsync(
                name, namedPrompt, tools, project, endpoint,
                modelParameters ?? modelParametersFactory(),
                isSystemAgent, cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await versionQueries.FindByStrictFingerprintAsync(project, systemPrompt, tools, cancellationToken);
            if (raced is not null)
            {
                return await raced.GetAgentAsync(cancellationToken);
            }
            throw;
        }
    }

    /// <summary>
    /// Override base AddAsync: a brand-new agent must be persisted together with its initial
    /// <see cref="IAgentVersion"/> (storage invariant). Caller-supplied Id and timestamps survive
    /// the operation.
    /// </summary>
    public override async Task<IAgent> AddAsync(IAgent entity, CancellationToken cancellationToken = default)
    {
        if (await this.ContainsAsync(entity.Id, cancellationToken))
        {
            throw new EntityAlreadyExistsException(entity.Id, typeof(IAgent));
        }
        return await PersistWithInitialVersionAsync(entity, cancellationToken);
    }

    /// <summary>
    /// Creates a new agent with the given name, system prompt, tools, and endpoint, persisting the agent
    /// and its initial version in a single transaction to satisfy the storage invariant.
    /// </summary>
    public Task<IAgent> CreateWithInitialVersionAsync(
        string name,
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        IProject project,
        IModelEndpoint endpoint,
        IModelParameters modelParameters,
        bool isSystemAgent,
        CancellationToken cancellationToken = default)
    {
        var agent = createNew(
            name: name,
            systemPrompt: systemPrompt,
            tools: tools,
            endpoint: endpoint,
            project: project,
            modelParameters: modelParameters,
            isSystemAgent: isSystemAgent);
        return PersistWithInitialVersionAsync(agent, cancellationToken);
    }

    private async Task<IAgent> PersistWithInitialVersionAsync(IAgent agent, CancellationToken cancellationToken)
    {
        Guid agentId = await transaction.InvokeAsync(async () =>
        {
            // The agent already carries its v1 (built inside the IAgent.CreateNew factory). Both
            // rows go in via a single SaveChanges — CurrentVersionId is a plain Guid column, no FK.
            var versionDomain = agent.CurrentVersion;

            Validator.ValidateObject(agent, new ValidationContext(agent), validateAllProperties: true);
            Validator.ValidateObject(versionDomain, new ValidationContext(versionDomain), validateAllProperties: true);

            var ctx = ambient.RequireContext();
            var agentEntity = await mapper.Map(agent, cancellationToken);
            agentEntity = agentEntity with { CurrentVersionId = versionDomain.Id };
            var versionEntity = await versionMapper.Value.Map(versionDomain, cancellationToken);

            ctx.Set<AgentEntity>().Add(agentEntity);
            ctx.Set<AgentVersionEntity>().Add(versionEntity);
            await ctx.SaveChangesAsync(cancellationToken);

            return agent.Id;
        });

        InvalidateCacheEntry(agentId);
        InvalidateVersionCache();
        return await this.GetAsync(agentId, cancellationToken);
    }

    /// <summary>
    /// Drops the agent-version cache now and again after the outermost transaction commits — the
    /// window described on <see cref="AbstractRepository{TDomainEntity,TStoredEntity}.InvalidateCacheEntry"/>
    /// applies to this second cache too when the write is nested in a larger logical unit.
    /// </summary>
    private void InvalidateVersionCache()
    {
        versionCache?.InvalidateAll();

        if (ambient.IsActive)
            ambient.RegisterPostCommit(() => versionCache?.InvalidateAll());
    }

    private Task SetCurrentVersionIdAsync(Guid agentId, Guid versionId, CancellationToken cancellationToken)
        => transaction.InvokeAsync(async () =>
        {
            var ctx = ambient.RequireContext();
            var stored = await ctx.Set<AgentEntity>().FirstAsync(a => a.Id == agentId, cancellationToken);
            var entry = ctx.Entry(stored);
            entry.Property(e => e.CurrentVersionId).CurrentValue = versionId;
            entry.Property(e => e.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
            // UpdatedAt is a concurrency token; align its original to the precision PostgreSQL persists
            // so this tracked update is not tripped by a sub-microsecond mismatch. See AbstractRepository.
            RealignConcurrencyToken(entry);
            await ctx.SaveChangesAsync(cancellationToken);
            InvalidateCacheEntry(agentId);
        });

    /// <summary>
    /// Computes the strict fingerprint (SHA-256 of system prompt plus sorted tools including descriptions)
    /// for the given system prompt and tool set. Used as the deduplication key in GetOrCreate races.
    /// </summary>
    public string GetAgentFingerprint(IPromptTemplate systemPrompt, IReadOnlyCollection<ToolSpecification> tools)
        => fingerprinter.Strict(systemPrompt, tools);

    /// <summary>
    /// Computes the strict fingerprint for the given agent's current system prompt and tool set.
    /// </summary>
    public string GetAgentFingerprint(IAgent agent)
        => GetAgentFingerprint(agent.SystemPrompt, agent.Tools);

    /// <summary>
    /// Updates the CurrentVersionId column of the agent row to point to the given version, also advancing
    /// UpdatedAt. Invalidates both the agent cache entry and the version cache.
    /// </summary>
    public Task SetCurrentVersionAsync(Guid agentId, Guid versionId, CancellationToken cancellationToken = default)
        => SetCurrentVersionIdAsync(agentId, versionId, cancellationToken);

    /// <summary>
    /// Returns the total number of non-system, non-archived agents across all projects. Used by the
    /// licensing layer to enforce the per-tier agent count limit.
    /// </summary>
    public async Task<int> CountNonSystemAsync(CancellationToken cancellationToken = default)
        => await contextFactory()
            .Set<AgentEntity>()
            .AsNoTracking()
            // Archived agents are soft-deleted — they must not consume a licensed agent slot.
            .CountAsync(e => !e.IsSystemAgent && !e.IsArchived, cancellationToken);

    /// <summary>
    /// Returns the agent in the given project with the exact display name, or null if no match exists.
    /// Name uniqueness within a project is enforced by a database constraint.
    /// </summary>
    public async Task<IAgent?> FindByNameAsync(IProject project, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);

        var id = await contextFactory()
            .Set<AgentEntity>()
            .AsNoTracking()
            .Where(e => e.Project == project.Id && e.Name == name)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return id is { } agentId ? await this.GetAsync(agentId, cancellationToken) : null;
    }

    /// <summary>
    /// Returns all non-archived agents belonging to the given project, in no particular order.
    /// </summary>
    public async Task<IReadOnlyList<IAgent>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<AgentEntity>()
            .AsNoTracking()
            .Where(e => e.Project == projectId)
            .ExcludeArchived()
            .ToListAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Returns the project ID for the given agent, or null if the agent does not exist.
    /// Projects only the FK column — no full entity mapping.
    /// </summary>
    public async Task<Guid?> GetProjectIdAsync(Guid agentId, CancellationToken cancellationToken = default)
        => await contextFactory()
            .Set<AgentEntity>()
            .AsNoTracking()
            .Where(e => e.Id == agentId)
            .Select(e => (Guid?)e.Project)
            .FirstOrDefaultAsync(cancellationToken);
}
