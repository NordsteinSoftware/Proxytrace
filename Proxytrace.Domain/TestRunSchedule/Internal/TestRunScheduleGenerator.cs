using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.TestRunSchedule.Internal;

internal class TestRunScheduleGenerator : DomainEntityGenerator<ITestRunSchedule>
{
    private readonly ITestRunSchedule.CreateNew factory;
    private readonly IDomainEntityGenerator<ITestSuite> suiteGenerator;
    private readonly IDomainEntityGenerator<IModelEndpoint> endpointGenerator;

    private static readonly string[] Names =
    [
        "Nightly", "Hourly Smoke", "Daily Regression", "Weekly Full", "Continuous"
    ];

    public TestRunScheduleGenerator(
        ITestRunSchedule.CreateNew factory,
        IRepository<ITestRunSchedule> repository,
        IDomainEntityGenerator<ITestSuite> suiteGenerator,
        IDomainEntityGenerator<IModelEndpoint> endpointGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.suiteGenerator = suiteGenerator;
        this.endpointGenerator = endpointGenerator;
    }

    public override async Task<ITestRunSchedule> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var suite = await suiteGenerator.GetOrCreateAsync(cancellationToken);
        var endpoint = await endpointGenerator.GetOrCreateAsync(cancellationToken);
        var name = random.Any(Names);

        return factory(
            name: name,
            suite: suite,
            endpoints: [endpoint],
            interval: TimeSpan.FromHours(24),
            isEnabled: true,
            anchorAt: DateTimeOffset.UtcNow);
    }
}
