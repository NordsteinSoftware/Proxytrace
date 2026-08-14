using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluator;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestRunDtoMapperTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ToDto_RunWithResults_DurationMsIsAveragePerCaseInferenceLatency()
    {
        IServiceProvider services = GetServices();

        // A run over two cases whose inference latencies are 1000ms and 3000ms. The run-level
        // "latency" must be their average (2000ms) — the aggregated per-case inference latency —
        // NOT a wall-clock (CompletedAt - StartedAt) timer, which in this fast test would be a
        // few ms, nowhere near 2000.
        ITestRun run = await BuildCompletedRunAsync(
            services,
            [TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(3000)],
            CancellationToken);

        var mapper = services.GetRequiredService<TestRunDtoMapper>();
        var dto = mapper.ToDto(run);

        dto.DurationMs.Should().Be(2000);
    }

    [TestMethod]
    public async Task ToDto_RunWithoutResults_DurationMsIsNull()
    {
        IServiceProvider services = GetServices();

        ITestRun run = await BuildCompletedRunAsync(services, latencies: [], CancellationToken);

        var mapper = services.GetRequiredService<TestRunDtoMapper>();
        var dto = mapper.ToDto(run);

        dto.DurationMs.Should().BeNull();
    }

    [TestMethod]
    public async Task RunLatency_AverageInferenceMs_AggregatesPerCaseLatency()
    {
        IServiceProvider services = GetServices();

        // Both TestRunDtoMapper and the A/B-test proposal mapper surface a run's latency through this
        // one helper, so guarding it here covers every run-level latency surface in the API. The value
        // is the average per-case inference latency (2000ms), not a wall-clock run timer.
        ITestRun run = await BuildCompletedRunAsync(
            services,
            [TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(3000)],
            CancellationToken);

        RunLatency.AverageInferenceMs(run).Should().Be(2000);
        RunLatency.AverageInferenceMs(await BuildCompletedRunAsync(services, latencies: [], CancellationToken))
            .Should().BeNull();
    }

    /// Builds a run whose i-th case produced a result with <paramref name="latencies"/>[i]; the
    /// number of latencies sets the suite's case count, so passing none yields a result-less run.
    private static async Task<ITestRun> BuildCompletedRunAsync(
        IServiceProvider services,
        IReadOnlyList<TimeSpan> latencies,
        CancellationToken cancellationToken)
    {
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var evaluatorGenerator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var endpointGenerator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var createTestCase = services.GetRequiredService<ITestCase.CreateNew>();
        var testCaseRepo = services.GetRequiredService<IRepository<ITestCase>>();
        var createTestSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var testSuiteRepo = services.GetRequiredService<IRepository<ITestSuite>>();
        var createGroup = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var groupRepo = services.GetRequiredService<ITestRunGroupRepository>();
        var createRun = services.GetRequiredService<ITestRun.CreateNew>();
        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var createTestResult = services.GetRequiredService<ITestResult.CreateNew>();
        var testResultRepo = services.GetRequiredService<IRepository<ITestResult>>();

        var agent = await agentGenerator.GetOrCreateAsync(cancellationToken);
        var evaluator = await evaluatorGenerator.GetOrCreateAsync(cancellationToken);
        var endpoint = await endpointGenerator.GetOrCreateAsync(cancellationToken);

        var input = Conversation.Create().With(new UserMessage([Content.FromText("What is the capital of France?")]));
        var expected = new AssistantMessage([Content.FromText("Paris")], []);

        var cases = new List<ITestCase>();
        for (var i = 0; i < Math.Max(1, latencies.Count); i++)
        {
            var testCase = createTestCase(input, expected, sourceAgentCallId: null);
            await testCaseRepo.AddAsync(testCase, cancellationToken);
            cases.Add(testCase);
        }

        var suite = createTestSuite("Latency Suite", agent, [evaluator], cases);
        await testSuiteRepo.AddAsync(suite, cancellationToken);

        var group = await groupRepo.AddAsync(createGroup(suite, false, null, 1), cancellationToken);
        var run = await runRepo.AddAsync(createRun(group, endpoint, 0), cancellationToken);

        for (var i = 0; i < latencies.Count; i++)
        {
            var completion = createCompletion(new AssistantMessage([Content.FromText("Paris")], []), null, latencies[i]);
            var result = createTestResult(cases[i], completion, []);
            await testResultRepo.AddAsync(result, cancellationToken);
            run = await run.SetTestResult(result, cancellationToken);
        }

        return run;
    }
}
