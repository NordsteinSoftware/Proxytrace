using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunGroup.Internal;

internal class TestRunGroupGenerator : DomainEntityGenerator<ITestRunGroup>
{
    private readonly ITestRunGroup.CreateNew factory;
    private readonly IDomainEntityGenerator<ITestSuite> suiteGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestRunGroupGenerator"/> class.
    /// </summary>
    public TestRunGroupGenerator(
        ITestRunGroup.CreateNew factory,
        IRepository<ITestRunGroup> repository,
        IDomainEntityGenerator<ITestSuite> suiteGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.suiteGenerator = suiteGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<ITestRunGroup> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var suite = await suiteGenerator.GetOrCreateAsync(cancellationToken);
        return factory(suite, isSystemRun: false, scheduleId: null, sampleCount: 1);
    }
}
