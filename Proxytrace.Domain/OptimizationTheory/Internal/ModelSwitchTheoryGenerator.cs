using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

internal class ModelSwitchTheoryGenerator : OptimizationTheoryGeneratorBase<IModelSwitchTheory>
{
    private readonly IModelSwitchTheory.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IDomainEntityGenerator<ITestSuite> suiteGenerator;
    private readonly IDomainEntityGenerator<IModelEndpoint> endpointGenerator;
    private readonly IRandom random;

    public ModelSwitchTheoryGenerator(
        IModelSwitchTheory.CreateNew factory,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IDomainEntityGenerator<ITestSuite> suiteGenerator,
        IDomainEntityGenerator<IModelEndpoint> endpointGenerator,
        IRepository<IOptimizationTheory> repository,
        IRandom random) : base(repository)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.suiteGenerator = suiteGenerator;
        this.endpointGenerator = endpointGenerator;
        this.random = random;
    }

    public override async Task<IModelSwitchTheory> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var suite = await suiteGenerator.GetOrCreateAsync(cancellationToken);
        var endpoint = await endpointGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            agent: agent,
            suite: suite,
            source: TheorySource.Optimizer,
            priority: Priority.Medium,
            rationale: random.String(),
            proposedEndpoint: endpoint,
            evidenceTestRunIds: []);
    }
}
