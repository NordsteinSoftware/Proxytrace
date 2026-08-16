using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator.Internal;

internal class ExactMatchEvaluatorGenerator : EvaluatorGeneratorBase<IExactMatchEvaluator>
{
    private readonly IExactMatchEvaluator.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactMatchEvaluatorGenerator"/> class.
    /// </summary>
    public ExactMatchEvaluatorGenerator(
        IExactMatchEvaluator.CreateNew factory,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRepository<IEvaluator> repository) : base(repository)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IExactMatchEvaluator> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IProject project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory(project);
    }
}
