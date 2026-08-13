using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestResult.Internal;

internal class TestResultGenerator : DomainEntityGenerator<ITestResult>, ITestResultGenerator
{
    private readonly ITestResult.CreateNew factory;
    private readonly IDomainEntityGenerator<ITestCase> testCaseGenerator;
    private readonly IDomainObjectGenerator<IEvaluation> evaluationGenerator;
    private readonly IDomainObjectGenerator<ICompletion> completionGenerator;

    public TestResultGenerator(
        ITestResult.CreateNew factory,
        IRepository<ITestResult> repository,
        IDomainEntityGenerator<ITestCase> testCaseGenerator,
        IDomainObjectGenerator<IEvaluation> evaluationGenerator,
        IDomainObjectGenerator<ICompletion> completionGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.testCaseGenerator = testCaseGenerator;
        this.evaluationGenerator = evaluationGenerator;
        this.completionGenerator = completionGenerator;
    }

    public override async Task<ITestResult> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<IEvaluation> evaluations = await Enumerable.Range(0, random.Int(1, 3))
            .Select(_ => evaluationGenerator.CreateAsync(cancellationToken))
            .Await();

        return factory(
            testCase: await testCaseGenerator.CreateAsync(cancellationToken),
            completion: await completionGenerator.CreateAsync(cancellationToken),
            evaluations: evaluations);
    }

    public async Task<ITestResult> CreateAsync(ITestCase testCase, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<IEvaluation> evaluations = await Enumerable.Range(0, random.Int(1, 3))
            .Select(_ => evaluationGenerator.CreateAsync(cancellationToken))
            .Await();

        var result = factory(
            testCase: testCase,
            completion: await completionGenerator.CreateAsync(cancellationToken),
            evaluations: evaluations);
        return await repository.AddAsync(result, cancellationToken);
    }
}
