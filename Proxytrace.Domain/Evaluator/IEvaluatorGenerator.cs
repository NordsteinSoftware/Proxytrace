namespace Proxytrace.Domain.Evaluator;

/// <summary>
/// Generates evaluator instances.
/// </summary>
public interface IEvaluatorGenerator : IDomainEntityGenerator<IEvaluator>
{
    Task<IEvaluator> CreateAsync(EvaluatorKind kind, CancellationToken cancellationToken = default);
}
