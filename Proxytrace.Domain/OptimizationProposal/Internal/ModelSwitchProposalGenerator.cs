using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

internal class ModelSwitchProposalGenerator : OptimizationProposalGeneratorBase<IModelSwitchProposal>
{
    private readonly IModelSwitchProposal.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IDomainEntityGenerator<IModelEndpoint> endpointGenerator;
    private readonly IDomainEntityGenerator<ITestRun> testRunGenerator;
    private readonly IRandom random;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSwitchProposalGenerator"/> class.
    /// </summary>
    public ModelSwitchProposalGenerator(
        IModelSwitchProposal.CreateNew factory,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IDomainEntityGenerator<IModelEndpoint> endpointGenerator,
        IDomainEntityGenerator<ITestRun> testRunGenerator,
        IRepository<IOptimizationProposal> repository,
        IRandom random) : base(repository)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.endpointGenerator = endpointGenerator;
        this.testRunGenerator = testRunGenerator;
        this.random = random;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IModelSwitchProposal> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var endpoint = await endpointGenerator.GetOrCreateAsync(cancellationToken);
        var abTestRun = await testRunGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            agent: agent,
            priority: Priority.Medium,
            rationale: random.String(),
            proposedEndpoint: endpoint,
            currentPassRate: 0.7,
            proposedPassRate: 0.8,
            expectedCostDelta: null,
            expectedLatencyDelta: null,
            evidenceTestRunIds: [],
            abTestRun: abTestRun);
    }
}
