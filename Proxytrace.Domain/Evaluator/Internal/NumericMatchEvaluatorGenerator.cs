using System.Text.RegularExpressions;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator.Internal;

internal class NumericMatchEvaluatorGenerator : EvaluatorGeneratorBase<INumericMatchEvaluator>
{
    private readonly INumericMatchEvaluator.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumericMatchEvaluatorGenerator"/> class.
    /// </summary>
    public NumericMatchEvaluatorGenerator(
        INumericMatchEvaluator.CreateNew factory,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRepository<IEvaluator> repository) : base(repository)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<INumericMatchEvaluator> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IProject project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory(new Regex(@"\d+(?:\.\d+)?"), 0.01m, project);
    }
}
