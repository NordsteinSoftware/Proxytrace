using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.CustomAnomaly.Internal;

internal class CustomAnomalyDetectorGenerator : DomainEntityGenerator<ICustomAnomalyDetector>
{
    private readonly ICustomAnomalyDetector.CreateNew factory;
    private readonly IAgentGenerator agentGenerator;

    public CustomAnomalyDetectorGenerator(
        ICustomAnomalyDetector.CreateNew factory,
        IRepository<ICustomAnomalyDetector> repository,
        IAgentGenerator agentGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
    }

    public override async Task<ICustomAnomalyDetector> GenerateAsync(CancellationToken cancellationToken = default)
    {
        // The review judge must be a system agent (mirrors the agentic evaluator generator).
        IAgent agent = await agentGenerator.CreateAsync(
            random.String(), isSystemAgent: true, cancellationToken: cancellationToken);

        return factory(
            name: random.String(),
            agent: agent,
            triggers: [new AnomalyTrigger(TriggerKind.Phrase, "refund")],
            allAgents: true,
            scopedAgents: [],
            isEnabled: true,
            blockUpstream: false);
    }
}
