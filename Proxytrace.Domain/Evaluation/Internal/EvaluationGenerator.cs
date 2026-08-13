using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Evaluation.Internal;

internal class EvaluationGenerator : DomainObjectGenerator<IEvaluation>
{
    private readonly IDomainEntityGenerator<IEvaluator> evaluatorGenerator;
    private readonly IEvaluation.Create factory;
    private readonly IEvaluation.CreateErrored erroredFactory;

    public EvaluationGenerator(
        IRandom random,
        IDomainEntityGenerator<IEvaluator> evaluatorGenerator,
        IEvaluation.Create factory,
        IEvaluation.CreateErrored erroredFactory) : base(random)
    {
        this.evaluatorGenerator = evaluatorGenerator;
        this.factory = factory;
        this.erroredFactory = erroredFactory;
    }

    public override async Task<IEvaluation> CreateAsync(CancellationToken cancellationToken = default)
    {
        IEvaluator evaluator = await evaluatorGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            evaluator,
            random.Enum<EvaluationScore>(),
            TimeSpan.FromMilliseconds(random.Int(1, 5_000)),
            reasoning: random.String());
    }

    public async Task<IEvaluation> CreateErroredAsync(CancellationToken cancellationToken = default)
    {
        IEvaluator evaluator = await evaluatorGenerator.GetOrCreateAsync(cancellationToken);
        return erroredFactory(evaluator, TimeSpan.FromMilliseconds(random.Int(1, 5_000)), new InvalidOperationException(random.String()));
    }
}
