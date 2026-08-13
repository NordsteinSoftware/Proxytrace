using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Evaluator.Internal;

internal class EvaluatorGenerator : DomainEntityGenerator<IEvaluator>, IEvaluatorGenerator
{
    private readonly IExactMatchEvaluator.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;
    private readonly IDomainEntityGenerator<IAgenticEvaluator> agenticGenerator;
    private readonly IDomainEntityGenerator<IExactMatchEvaluator> exactMatchGenerator;
    private readonly IDomainEntityGenerator<INumericMatchEvaluator> numericMatchGenerator;
    private readonly IDomainEntityGenerator<IJsonSchemaMatchEvaluator> jsonSchemaMatchGenerator;

    public EvaluatorGenerator(
        IExactMatchEvaluator.CreateNew factory,
        IRepository<IEvaluator> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IDomainEntityGenerator<IAgenticEvaluator> agenticGenerator,
        IDomainEntityGenerator<IExactMatchEvaluator> exactMatchGenerator,
        IDomainEntityGenerator<INumericMatchEvaluator> numericMatchGenerator,
        IDomainEntityGenerator<IJsonSchemaMatchEvaluator> jsonSchemaMatchGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
        this.agenticGenerator = agenticGenerator;
        this.exactMatchGenerator = exactMatchGenerator;
        this.numericMatchGenerator = numericMatchGenerator;
        this.jsonSchemaMatchGenerator = jsonSchemaMatchGenerator;
    }

    public override async Task<IEvaluator> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory(project);
    }

    public async Task<IEvaluator> CreateAsync(EvaluatorKind kind, CancellationToken cancellationToken = default) 
        => kind switch
        {
            EvaluatorKind.Agentic => await agenticGenerator.CreateAsync(cancellationToken),
            EvaluatorKind.ExactMatch => await exactMatchGenerator.CreateAsync(cancellationToken),
            EvaluatorKind.NumericMatch => await numericMatchGenerator.CreateAsync(cancellationToken),
            EvaluatorKind.JsonSchemaMatch => await jsonSchemaMatchGenerator.CreateAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
