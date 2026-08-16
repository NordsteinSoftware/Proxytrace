using Nordstein.Core.Common.Random;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.CustomAnomaly.Internal;

internal class CustomAnomalyResultGenerator : DomainEntityGenerator<ICustomAnomalyResult>
{
    private readonly ICustomAnomalyResult.CreateNew factory;
    private readonly IDomainEntityGenerator<ICustomAnomalyDetector> detectorGenerator;
    private readonly IDomainEntityGenerator<IAgentCall> callGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomAnomalyResultGenerator"/> class.
    /// </summary>
    public CustomAnomalyResultGenerator(
        ICustomAnomalyResult.CreateNew factory,
        IRepository<ICustomAnomalyResult> repository,
        IDomainEntityGenerator<ICustomAnomalyDetector> detectorGenerator,
        IDomainEntityGenerator<IAgentCall> callGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.detectorGenerator = detectorGenerator;
        this.callGenerator = callGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<ICustomAnomalyResult> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var detector = await detectorGenerator.GetOrCreateAsync(cancellationToken);
        var call = await callGenerator.GetOrCreateAsync(cancellationToken);

        return factory(
            detectorId: detector.Id,
            agentCallId: call.Id,
            projectId: call.Agent.Project.Id,
            matchedTrigger: "refund",
            reasoning: "The assistant promised a refund it cannot grant.");
    }
}
