namespace Proxytrace.Domain.Evaluator.Internal;

internal abstract class EvaluatorGeneratorBase<T> : IDomainEntityGenerator<T>
    where T : class, IEvaluator
{
    private readonly IRepository<IEvaluator> repository;

    protected EvaluatorGeneratorBase(IRepository<IEvaluator> repository)
    {
        this.repository = repository;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public abstract Task<T> GenerateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates asynchronously.
    /// </summary>
    public async Task<T> CreateAsync(CancellationToken cancellationToken = default)
    {
        var instance = await GenerateAsync(cancellationToken);
        return (T)await repository.AddAsync(instance, cancellationToken);
    }

    /// <summary>
    /// Gets the or create asynchronously.
    /// </summary>
    public async Task<T> GetOrCreateAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindFirstAsync(cancellationToken);
        if (existing is T match)
            return match;
        return await CreateAsync(cancellationToken);
    }
}
