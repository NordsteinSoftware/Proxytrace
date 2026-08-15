using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;

namespace Proxytrace.Domain.Evaluator.Internal;

internal class AgenticEvaluatorGenerator : EvaluatorGeneratorBase<IAgenticEvaluator>
{
    private readonly IAgentGenerator agentGenerator;
    private readonly IAgenticEvaluator.CreateNew factory;
    private readonly IRandom random;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgenticEvaluatorGenerator"/> class.
    /// </summary>
    public AgenticEvaluatorGenerator(
        IAgentGenerator agentGenerator,
        IAgenticEvaluator.CreateNew factory,
        IRandom random,
        IRepository<IEvaluator> repository) : base(repository)
    {
        this.agentGenerator = agentGenerator;
        this.factory = factory;
        this.random = random;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IAgenticEvaluator> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IAgent agent = await agentGenerator.CreateAsync(random.String(), isSystemAgent: true, cancellationToken: cancellationToken);
        return factory(agent);
    }
}
