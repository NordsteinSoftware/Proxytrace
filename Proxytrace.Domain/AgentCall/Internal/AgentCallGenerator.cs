using System.Net;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;

namespace Proxytrace.Domain.AgentCall.Internal;

internal class AgentCallGenerator : DomainEntityGenerator<IAgentCall>, IAgentCallGenerator
{
    private readonly IAgentCall.CreateNew factory;
    private readonly IAgentCall.CreateExisting createExisting;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IAgentVersionRepository versionRepository;
    private readonly IDomainObjectGenerator<IModelEndpoint> endpointGenerator;
    private readonly IDomainObjectGenerator<Conversation> conversationGenerator;
    private readonly IDomainObjectGenerator<ICompletion> completionGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallGenerator"/> class.
    /// </summary>
    public AgentCallGenerator(
        IAgentCall.CreateNew factory,
        IAgentCall.CreateExisting createExisting,
        IRepository<IAgentCall> repository,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IAgentVersionRepository versionRepository,
        IDomainObjectGenerator<IModelEndpoint> endpointGenerator,
        IDomainObjectGenerator<Conversation> conversationGenerator,
        IDomainObjectGenerator<ICompletion> completionGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.createExisting = createExisting;
        this.agentGenerator = agentGenerator;
        this.versionRepository = versionRepository;
        this.endpointGenerator = endpointGenerator;
        this.conversationGenerator = conversationGenerator;
        this.completionGenerator = completionGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IAgentCall> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.CreateAsync(cancellationToken);
        var version = agent.CurrentVersion;

        return factory(
            agent: agent,
            version: version,
            endpoint: await endpointGenerator.CreateAsync(cancellationToken),
            request: await conversationGenerator.CreateAsync(cancellationToken),
            response: await completionGenerator.CreateAsync(cancellationToken),
            httpStatus: HttpStatusCode.OK,
            finishReason: "stop",
            errorMessage: null,
            conversationId: null);
    }

    /// <summary>
    /// Creates asynchronously.
    /// </summary>
    public async Task<IAgentCall> CreateAsync(DateTimeOffset createdAt, CancellationToken cancellationToken = default)
    {
        var agentCall = await CreateAsync(cancellationToken);
        var modified = createExisting(
            agent: agentCall.Agent,
            version: agentCall.Version,
            endpoint: agentCall.Endpoint,
            request: agentCall.Request,
            response: agentCall.Response,
            httpStatus: agentCall.HttpStatus,
            finishReason: agentCall.FinishReason,
            errorMessage: agentCall.ErrorMessage,
            modelParameters: agentCall.ModelParameters,
            conversationId: agentCall.ConversationId,
            outlierFlags: agentCall.OutlierFlags,
            existing: new ModifiedDomainEntityData(agentCall.Id, CreatedAt: createdAt, agentCall.UpdatedAt));
        return await modified.UpdateAsync(cancellationToken);
    }

    private record ModifiedDomainEntityData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;
}
