using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator.Internal;

internal class JsonSchemaMatchEvaluatorGenerator : EvaluatorGeneratorBase<IJsonSchemaMatchEvaluator>
{
    private readonly IJsonSchemaMatchEvaluator.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonSchemaMatchEvaluatorGenerator"/> class.
    /// </summary>
    public JsonSchemaMatchEvaluatorGenerator(
        IJsonSchemaMatchEvaluator.CreateNew factory,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRepository<IEvaluator> repository) : base(repository)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IJsonSchemaMatchEvaluator> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IProject project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory("""{"type": "object"}""", project);
    }
}
