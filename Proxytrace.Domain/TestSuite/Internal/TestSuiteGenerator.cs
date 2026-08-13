using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.Domain;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestSuite.Internal;

internal class TestSuiteGenerator : DomainEntityGenerator<ITestSuite>
{
    private readonly ITestSuite.CreateNew factory;
    private readonly IDomainEntityGenerator<IAgent> agentGenerator;
    private readonly IDomainEntityGenerator<IEvaluator> evaluatorGenerator;
    private readonly IDomainEntityGenerator<ITestCase> testCaseGenerator;

    private static readonly string[] Names =
    [
        "Core Flows", "Edge Cases", "Regression", "Smoke Tests", "Happy Path",
        "Error Handling", "Integration", "Boundary Tests", "Sanity Check", "Full Coverage"
    ];

    public TestSuiteGenerator(
        ITestSuite.CreateNew factory,
        IRepository<ITestSuite> repository,
        IDomainEntityGenerator<IAgent> agentGenerator,
        IDomainEntityGenerator<IEvaluator> evaluatorGenerator,
        IDomainEntityGenerator<ITestCase> testCaseGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.agentGenerator = agentGenerator;
        this.evaluatorGenerator = evaluatorGenerator;
        this.testCaseGenerator = testCaseGenerator;
    }

    public override async Task<ITestSuite> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var evaluator = await evaluatorGenerator.GetOrCreateAsync(cancellationToken);
        var testCase = await testCaseGenerator.CreateAsync(cancellationToken);
        var name = random.Any(Names);
        return factory(name: name, agent: agent, evaluators: [evaluator], testCases: [testCase]);
    }
}
