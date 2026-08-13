using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

internal class SystemPromptTheoryGenerator : OptimizationTheoryGeneratorBase<ISystemPromptTheory>
{
    private readonly ISystemPromptTheory.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IDomainEntityGenerator<ITestSuite> suiteGenerator;
    private readonly IRandom random;

    public SystemPromptTheoryGenerator(
        ISystemPromptTheory.CreateNew factory,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IDomainEntityGenerator<ITestSuite> suiteGenerator,
        IRepository<IOptimizationTheory> repository,
        IRandom random) : base(repository)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.suiteGenerator = suiteGenerator;
        this.random = random;
    }

    public override async Task<ISystemPromptTheory> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var suite = await suiteGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            agent: agent,
            suite: suite,
            source: TheorySource.Optimizer,
            priority: Priority.Medium,
            rationale: random.String(),
            proposedSystemMessage: random.String(),
            evidenceTestRunIds: []);
    }
}
