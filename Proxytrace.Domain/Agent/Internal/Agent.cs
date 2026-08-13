using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Inference;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.Tools;

namespace Proxytrace.Domain.Agent.Internal;

internal record Agent : DomainEntity<IAgent>, IAgent
{
    private readonly IModelClient.Factory modelClientFactory;
    private readonly ILogger<IAgent> logger;
    private readonly IAgentVersion.CreateNew createVersion;
    private readonly Lazy<IAgentVersionRepository> versionRepository;
    private readonly Lazy<IAgentRepository> agentRepository;
    private readonly IAsyncLock locker;

    public string Name { get; }
    public IModelEndpoint Endpoint { get; private init; }
    public IProject Project { get; }
    public IModelParameters ModelParameters { get; private init; }
    public bool IsSystemAgent { get; }
    private IAgentVersion? CurrentVersion { get; init; }

    IAgentVersion IAgent.CurrentVersion 
        => CurrentVersion
           ?? throw new InvalidOperationException(
               $"Agent {Id} ({Name}) has no current version. " +
               "This should only be observable inside the IAgent.CreateNew factory before WithInitialVersion is called.");

    public IPromptTemplate SystemPrompt
        => ((IAgent)this).CurrentVersion.SystemPrompt;

    public IReadOnlyList<ToolSpecification> Tools
        => ((IAgent)this).CurrentVersion.Tools;

    /// <summary>
    /// Shell constructor used only by the <see cref="IAgent.CreateNew"/> factory in
    /// <c>Proxytrace.Domain.Module</c>. Produces an agent with a freshly-minted <c>Id</c> but
    /// no <see cref="CurrentVersion"/>; the factory immediately calls <see cref="WithInitialVersion"/>
    /// to stitch in v1 before the agent escapes. External callers never observe a shell agent.
    /// </summary>
    public Agent(
        string name,
        IModelEndpoint endpoint,
        IProject project,
        IModelParameters modelParameters,
        bool isSystemAgent,
        IRepository<IAgent> repository,
        IModelClient.Factory modelClientFactory,
        IAgentVersion.CreateNew createVersion,
        Lazy<IAgentVersionRepository> versionRepository,
        Lazy<IAgentRepository> agentRepository,
        IAsyncLock locker) : base(repository)
    {
        this.modelClientFactory = modelClientFactory;
        this.createVersion = createVersion;
        this.versionRepository = versionRepository;
        this.agentRepository = agentRepository;
        this.locker = locker;
        this.logger = NullLogger<IAgent>.Instance;

        Name = name;
        Project = project;
        Endpoint = endpoint;
        ModelParameters = modelParameters;
        IsSystemAgent = isSystemAgent;
        CurrentVersion = null;
    }

    /// <summary>
    /// Constructor for reconstituting an agent from storage.
    /// </summary>
    public Agent(
        string name,
        IProject project,
        IModelEndpoint endpoint,
        bool isSystemAgent,
        IModelParameters modelParameters,
        IAgentVersion currentVersion,
        IDomainEntityData existing,
        IRepository<IAgent> repository,
        IModelClient.Factory modelClientFactory,
        IAgentVersion.CreateNew createVersion,
        Lazy<IAgentVersionRepository> versionRepository,
        Lazy<IAgentRepository> agentRepository,
        IAsyncLock locker,
        ILogger<IAgent> logger) : base(existing, repository)
    {
        this.modelClientFactory = modelClientFactory;
        this.createVersion = createVersion;
        this.versionRepository = versionRepository;
        this.agentRepository = agentRepository;
        this.locker = locker;
        this.logger = logger;

        Name = name;
        Project = project;
        Endpoint = endpoint;
        ModelParameters = modelParameters;
        IsSystemAgent = isSystemAgent;
        CurrentVersion = currentVersion;
    }

    /// <summary>Stitches an in-memory v1 onto a shell agent. Called by the
    /// <see cref="IAgent.CreateNew"/> factory exactly once per new agent.</summary>
    internal Agent WithInitialVersion(IAgentVersion version)
        => this with { CurrentVersion = version };

    public IModelClient CreateClient(
        IModelEndpoint? customEndpoint = null,
        bool skipIngestion = false)
        => modelClientFactory(this, customEndpoint, skipIngestion: skipIngestion);

    public async Task<IAgent> CreateNewVersionAsync(
        IPromptTemplate systemPrompt,
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default)
    {
        // Process-local lock only — coordinates concurrent CreateNewVersionAsync calls within one
        // instance so we don't compute the same `next` twice. Multi-replica deployments rely on
        // the unique (AgentId, VersionNumber) index + DbUpdateException-driven requeue in
        // AgentCallProcessor.IsRetryable to handle cross-process races.
        using IDisposable lockObj = await locker.LockAsync($"agent-versions:{Id}", cancellationToken);
        var existingVersions = await versionRepository.Value.GetByAgentAsync(this, cancellationToken);
        int next = existingVersions.Count == 0 ? 1 : existingVersions.Max(v => v.VersionNumber) + 1;
        var version = createVersion(Project.Id, Id, next, systemPrompt, tools);
        version = await version.AddAsync(cancellationToken);
        await agentRepository.Value.SetCurrentVersionAsync(Id, version.Id, cancellationToken);
        return await ReloadAsync(cancellationToken);
    }

    public Task<IAgent> ChangeSystemMessage(
        IPromptTemplate systemPrompt,
        CancellationToken cancellationToken = default)
        => SystemPrompt.Equals(systemPrompt)
            ? Task.FromResult<IAgent>(this)
            : CreateNewVersionAsync(systemPrompt, Tools, cancellationToken);

    public Task<IAgent> ChangeTools(
        IReadOnlyList<ToolSpecification> tools,
        CancellationToken cancellationToken = default)
        => Tools.SequenceEqual(tools)
            ? Task.FromResult<IAgent>(this)
            : CreateNewVersionAsync(SystemPrompt, tools, cancellationToken);

    public Task<IAgent> ChangeEndpoint(IModelEndpoint modelEndpoint,
        CancellationToken cancellationToken = default)
    {
        if (modelEndpoint.Id == Endpoint.Id)
        {
            logger.LogWarning(
                "Attempted to change agent endpoint to the same endpoint (AgentId: {AgentId}, EndpointId: {EndpointId})",
                Id, modelEndpoint.Id);
            return Task.FromResult<IAgent>(this);
        }

        return ApplyAsync(this with { Endpoint = modelEndpoint }, cancellationToken);
    }

    public Task<IAgent> ChangeModelParameters(
        IModelParameters modelParameters,
        CancellationToken cancellationToken = default)
        => ModelParameters.Equals(modelParameters)
            ? Task.FromResult<IAgent>(this)
            : ApplyAsync(this with { ModelParameters = modelParameters }, cancellationToken);

    public SystemMessage CreateSystemMessage(IReadOnlyDictionary<string, string>? variables = null)
        => Message.Message.CreateSystemMessage(SystemPrompt, variables);

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.NotNullOrWhiteSpace(Name);

        foreach (var result in Endpoint.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in Project.Validate(validationContext))
        {
            yield return result;
        }

        // Shell agents (inside the CreateNew factory, pre-stitch) have no version yet.
        // The factory validates the stitched agent before persisting.
        if (CurrentVersion is null)
        {
            yield break;
        }

        yield return Validation.NotNull(SystemPrompt);
        foreach (var result in SystemPrompt.Validate(validationContext))
        {
            yield return result;
        }
    }
}
