using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

internal class ToolUpdateProposalGenerator : OptimizationProposalGeneratorBase<IToolUpdateProposal>
{
    private readonly IToolUpdateProposal.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IDomainEntityGenerator<ITestRun> testRunGenerator;
    private readonly IRandom random;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolUpdateProposalGenerator"/> class.
    /// </summary>
    public ToolUpdateProposalGenerator(
        IToolUpdateProposal.CreateNew factory,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IDomainEntityGenerator<ITestRun> testRunGenerator,
        IRepository<IOptimizationProposal> repository,
        IRandom random) : base(repository)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.testRunGenerator = testRunGenerator;
        this.random = random;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IToolUpdateProposal> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var abTestRun = await testRunGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            agent: agent,
            priority: Priority.Medium,
            rationale: random.String(),
            proposedTools: [],
            currentPassRate: 0.5,
            proposedPassRate: 0.7,
            evidenceTestRunIds: [],
            abTestRun: abTestRun);
    }
}
