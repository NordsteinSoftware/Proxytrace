using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Domain.TestRun.Internal;

internal class TestRunGenerator : DomainEntityGenerator<ITestRun>
{
    private readonly ITestRun.CreateNew factory;
    private readonly IDomainEntityGenerator<IModelEndpoint> endpointGenerator;
    private readonly IDomainEntityGenerator<ITestRunGroup> groupGenerator;
    private readonly ITestResultGenerator testResultGenerator;

    public TestRunGenerator(
        ITestRun.CreateNew factory,
        IRepository<ITestRun> repository,
        IDomainEntityGenerator<IModelEndpoint> endpointGenerator,
        IDomainEntityGenerator<ITestRunGroup> groupGenerator,
        ITestResultGenerator testResultGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.endpointGenerator = endpointGenerator;
        this.groupGenerator = groupGenerator;
        this.testResultGenerator = testResultGenerator;
    }

    public override async Task<ITestRun> GenerateAsync(CancellationToken cancellationToken = default)
    {
        ITestRun run = factory(
            group: await groupGenerator.GetOrCreateAsync(cancellationToken),
            endpoint: await endpointGenerator.GetOrCreateAsync(cancellationToken),
            sampleIndex: 0);

        int resultCount = random.Int(0, run.Group.Suite.TestCases.Count);
        IReadOnlyCollection<ITestResult> results = await Enumerable.Range(0, resultCount)
            .Select(i => testResultGenerator.CreateAsync(run.Group.Suite.TestCases.ElementAt(i), cancellationToken))
            .ToArray()
            .Await();

        foreach (ITestResult result in results)
        {
            await run.SetTestResult(result, cancellationToken);
        }

        return run;
    }
}
