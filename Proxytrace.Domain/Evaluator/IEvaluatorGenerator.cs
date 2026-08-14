namespace Proxytrace.Domain.Evaluator;

public interface IEvaluatorGenerator : IDomainEntityGenerator<IEvaluator>
{
    Task<IEvaluator> CreateAsync(EvaluatorKind kind, CancellationToken cancellationToken = default);
}
