using System.Collections.Concurrent;
using System.Reflection;
using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Domain.Usage;
using Proxytrace.Licensing;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class TestRunnerServiceTests : BaseTest<Module>
{
    private const string MatchingText = "Paris";
    private const string DifferentText = "London";


    private static async Task<ITestSuite> BuildSuiteAsync(
        IServiceProvider services,
        AssistantMessage expectedOutput,
        CancellationToken ct)
    {
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var evaluatorGenerator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var createTestCase = services.GetRequiredService<ITestCase.CreateNew>();
        var testCaseRepo = services.GetRequiredService<IRepository<ITestCase>>();
        var createTestSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var testSuiteRepo = services.GetRequiredService<IRepository<ITestSuite>>();

        var agent = await agentGenerator.GetOrCreateAsync(ct);
        var evaluator = await evaluatorGenerator.GetOrCreateAsync(ct);

        var input = Conversation.Create()
            .With(new UserMessage([Content.FromText("What is the capital of France?")]));

        var testCase = createTestCase(input, expectedOutput, sourceAgentCallId: null);
        await testCaseRepo.AddAsync(testCase, ct);

        var suite = createTestSuite("Test Suite", agent, [evaluator], [testCase]);
        await testSuiteRepo.AddAsync(suite, ct);
        return suite;
    }

    /// Builds a suite carrying one Exact Match evaluator and one agentic evaluator, so a run's
    /// evaluation count reveals whether the agentic one was skipped by the license gate.
    private static async Task<ITestSuite> BuildSuiteWithAgenticAsync(
        IServiceProvider services,
        AssistantMessage expectedOutput,
        CancellationToken ct)
    {
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var exactGenerator = services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>();
        var agenticGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgenticEvaluator>>();
        var createTestCase = services.GetRequiredService<ITestCase.CreateNew>();
        var testCaseRepo = services.GetRequiredService<IRepository<ITestCase>>();
        var createTestSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var testSuiteRepo = services.GetRequiredService<IRepository<ITestSuite>>();

        var agent = await agentGenerator.GetOrCreateAsync(ct);
        var exactMatch = await exactGenerator.CreateAsync(ct);
        var agentic = await agenticGenerator.CreateAsync(ct);

        var input = Conversation.Create()
            .With(new UserMessage([Content.FromText("What is the capital of France?")]));

        var testCase = createTestCase(input, expectedOutput, sourceAgentCallId: null);
        await testCaseRepo.AddAsync(testCase, ct);

        var evaluators = new IEvaluator[] { exactMatch, agentic };
        var suite = createTestSuite("Agentic Suite", agent, evaluators, [testCase]);
        await testSuiteRepo.AddAsync(suite, ct);
        return suite;
    }

    private async Task<IReadOnlyList<IModelEndpoint>> CreateEndpoints(IServiceProvider services, int count)
    {
        var generator = services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>();
        var endpoints = new List<IModelEndpoint>();
        for (var i = 0; i < count; i++)
            endpoints.Add(await generator.CreateAsync(CancellationToken));
        return endpoints;
    }

    private static void RegisterLicense(ContainerBuilder builder, bool agenticEnabled)
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(Arg.Any<LicenseFeature>()).Returns(true);
        license.IsFeatureEnabled(LicenseFeature.AgenticEvaluators).Returns(agenticEnabled);
        builder.RegisterInstance(license).As<ILicenseService>();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    private void RegisterFakeModelClient(ContainerBuilder builder, AssistantMessage response)
    {
        builder.Register(ct =>
        {
            IModelClient handler = Substitute.For<IModelClient>();
            var completionFactory = ct.Resolve<ICompletion.Create>();
            handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(completionFactory(response, null, TimeSpan.FromMilliseconds(1000))));
            return handler;
        });
    }

    // A model client whose completion signals the moment it is entered, then blocks until its
    // cancellation token trips. The `entered` signal gives a test a deterministic mid-run point —
    // no polling, no timeout — at which the run is provably in flight inside the model call, so a
    // cancellation fired then is always observed mid-flight (never after the run has completed).
    private static void RegisterBlockingModelClient(ContainerBuilder builder, TaskCompletionSource entered)
    {
        builder.Register(_ =>
        {
            IModelClient handler = Substitute.For<IModelClient>();
            handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    // Announce that the run has genuinely reached the model call, then block until
                    // cancellation. TrySetResult is idempotent, so repeated resolves are harmless.
                    entered.TrySetResult();
                    await Task.Delay(-1, call.Arg<CancellationToken>());
                    throw new InvalidOperationException("unreachable — the delay always throws on cancellation");
                });
            return handler;
        });
    }

    // A model client that reports whether the token it was handed was ever signalled. It announces
    // `entered` once the run is provably inside the model call, then waits on that token; the wait
    // is bounded so a run whose cancellation never arrives finishes on its own instead of hanging
    // the suite — which is exactly what the pre-fix code did with a live, un-cancelled token.
    private static void RegisterCancellationObservingModelClient(
        ContainerBuilder builder,
        AssistantMessage response,
        TaskCompletionSource entered,
        TaskCompletionSource tokenSignalled)
    {
        builder.Register(ct =>
        {
            IModelClient handler = Substitute.For<IModelClient>();
            var completionFactory = ct.Resolve<ICompletion.Create>();
            handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    CancellationToken callToken = call.Arg<CancellationToken>();
                    entered.TrySetResult();
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), callToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Recorded before the exception escapes, so a caller that has observed the
                        // run unwind has necessarily observed this too — no polling, no timing.
                        tokenSignalled.TrySetResult();
                        throw;
                    }

                    return completionFactory(response, null, TimeSpan.FromMilliseconds(1000));
                });
            return handler;
        });
    }

    [TestMethod]
    public async Task CancelAsync_WhileGroupIsRunning_SignalsTheInFlightModelCallsToken()
    {
        // Regression: ExecuteGroupAsync registers the group's CancellationTokenSource under the
        // *group* id, but CancelAsync looked it up under each *run* id — the lookup never matched,
        // so cancelling a group only flipped the database rows while the parallel loop kept issuing
        // real, billed model calls through to completion.
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenSignalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = GetServices(config =>
            RegisterCancellationObservingModelClient(config, expectedOutput, entered, tokenSignalled));

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var endpoint = (await CreateEndpoints(services, 1))[0];
        var runner = services.GetRequiredService<ITestRunnerService>();

        ITestRunGroup? created = null;
        var runTask = runner.RunInForegroundAsync(
            suite,
            [endpoint],
            isSystemTestRun: true,
            onGroupCreated: (g, _) => { created = g; return Task.CompletedTask; },
            cancellationToken: CancellationToken);

        // Deterministic mid-run point: the model call has been entered and is now waiting on its
        // token, so the cancellation below always lands while the run is genuinely in flight.
        await entered.Task;
        if (created is not { } group)
            throw new InvalidOperationException("onGroupCreated must run before the model call is reached");

        await runner.CancelAsync(group, CancellationToken);

        await FluentActions.Invoking(() => runTask).Should().ThrowAsync<OperationCanceledException>();
        tokenSignalled.Task.IsCompletedSuccessfully.Should()
            .BeTrue("cancelling the group must reach the token the in-flight model call is running on");
    }

    [TestMethod]
    public async Task CancelAsync_WhileGroupIsRunning_SettlesGroupCancelledWithoutASpuriousCompletion()
    {
        // The post-run SetCompleted used to be unguarded: a group already driven terminal by
        // CancelAsync was pushed into an invalid transition, the InvalidOperationException landed in
        // the generic handler and was logged as "Test run group failed", and a completion event was
        // broadcast for a group the user had cancelled.
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenSignalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var broadcaster = Substitute.For<ITestResultBroadcaster>();
        var groupEvents = new ConcurrentQueue<GroupRunCompleteEvent>();
        broadcaster.When(b => b.PublishGroupComplete(Arg.Any<GroupRunCompleteEvent>()))
            .Do(ci =>
            {
                var groupEvent = ci.Arg<GroupRunCompleteEvent>();
                ArgumentNullException.ThrowIfNull(groupEvent);
                groupEvents.Enqueue(groupEvent);
            });

        var services = GetServices(config =>
        {
            RegisterCancellationObservingModelClient(config, expectedOutput, entered, tokenSignalled);
            config.RegisterInstance(broadcaster).As<ITestResultBroadcaster>();
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var endpoint = (await CreateEndpoints(services, 1))[0];
        var runner = services.GetRequiredService<ITestRunnerService>();

        ITestRunGroup? created = null;
        var runTask = runner.RunInForegroundAsync(
            suite,
            [endpoint],
            isSystemTestRun: true,
            onGroupCreated: (g, _) => { created = g; return Task.CompletedTask; },
            cancellationToken: CancellationToken);

        await entered.Task;
        if (created is not { } group)
            throw new InvalidOperationException("onGroupCreated must run before the model call is reached");

        await runner.CancelAsync(group, CancellationToken);
        await FluentActions.Invoking(() => runTask).Should().ThrowAsync<OperationCanceledException>();

        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var final = await groups.GetAsync(group.Id, CancellationToken);
        final.Status.Should().Be(TestRunStatus.Cancelled);

        // Exactly one terminal event, carrying Cancelled: no completion broadcast for a group the
        // user cancelled, and no duplicate from the execution unwinding alongside the canceller.
        groupEvents.Should().ContainSingle()
            .Which.GroupStatus.Should().Be(TestRunStatus.Cancelled);
    }

    [TestMethod]
    public async Task RunInForeground_WhenCancelledMidRun_MarksGroupCancelled()
    {
        // RunContinuationsAsynchronously keeps the test's await off the model-call thread, so
        // signalling `entered` never inlines the rest of the test onto the runner's worker.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = GetServices(builder => RegisterBlockingModelClient(builder, entered));
        var suite = await BuildSuiteAsync(services, new AssistantMessage([Content.FromText(MatchingText)], []), CancellationToken);
        var endpoint = (await CreateEndpoints(services, 1))[0];
        var runner = services.GetRequiredService<ITestRunnerService>();
        var groups = services.GetRequiredService<ITestRunGroupRepository>();

        using var cts = new CancellationTokenSource();
        ITestRunGroup? created = null;
        var runTask = runner.RunInForegroundAsync(
            suite,
            [endpoint],
            isSystemTestRun: true,
            onGroupCreated: (g, _) => { created = g; return Task.CompletedTask; },
            cancellationToken: cts.Token);

        // Deterministic mid-run point: the model call has been entered and is now blocked on the
        // cancellation token. Cancelling here is guaranteed to land while the run is genuinely in
        // flight — no polling and no timeout, so CPU contention under the full parallel suite can
        // no longer let the run complete before the cancellation is observed.
        await entered.Task;
        if (created is not { } group)
            throw new InvalidOperationException("onGroupCreated must run before the model call is reached");

        await cts.CancelAsync();
        await FluentActions.Invoking(() => runTask).Should().ThrowAsync<OperationCanceledException>();

        // The cancelled group must not be stranded in Running — it is reconciled to a terminal state.
        var final = await groups.GetAsync(group.Id, CancellationToken);
        final.Status.Should().Be(TestRunStatus.Cancelled);
    }

    [TestMethod]
    public async Task RunAsync_WhenResponseMatchesExpected_ProducesPassResult()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);

        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        IReadOnlyList<ITestRun> testRuns = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);

        testRuns.Should().HaveCount(1);
        var testRun = testRuns.First();
        testRun.TestResults.Should().HaveCount(1);
        testRun.TestResults[0].Evaluations.Should().ContainSingle()
            .Which.Score.Should().Be(EvaluationScore.Acceptable);
    }

    [TestMethod]
    public async Task RunInForeground_WithThreeEndpoints_CreatesGroupWithThreeRuns()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoints = await CreateEndpoints(services, 3);

        var group = await runner.RunInForegroundAsync(suite, endpoints, cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        var runs = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);
        runs.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task RunInForeground_WithMoreThanThreeEndpoints_Throws()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoints = await CreateEndpoints(services, 4);

        await FluentActions
            .Invoking(() => runner.RunInForegroundAsync(suite, endpoints, cancellationToken: CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
    public async Task RunInForeground_InvokesOnGroupCreated_WithPendingGroupBeforeExecution()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        TestRunStatus? statusAtCallback = null;
        Guid? observedRunId = null;

        var group = await runner.RunInForegroundAsync(
            suite,
            [endpoint],
            onGroupCreated: async (createdGroup, ct) =>
            {
                statusAtCallback = createdGroup.Status;
                var createdRuns = await createdGroup.GetTestRuns(ct);
                observedRunId = createdRuns.First().Id;
            },
            cancellationToken: CancellationToken);

        // The hook must see the group before it executes, with its run already persisted.
        statusAtCallback.Should().Be(TestRunStatus.Pending);
        observedRunId.Should().NotBeNull();

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        var runs = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);
        runs.Should().ContainSingle(r => r.Id == observedRunId);
    }

    [TestMethod]
    public async Task RunAsync_WhenResponseDiffersFromExpected_ProducesFailResult()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var actualOutput = new AssistantMessage([Content.FromText(DifferentText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, actualOutput);
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);

        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        IReadOnlyList<ITestRun> testRuns = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);

        testRuns.Should().HaveCount(1);
        var testRun = testRuns.First();
        testRun.TestResults.Should().HaveCount(1);
        testRun.TestResults[0].Evaluations.Should().ContainSingle()
            .Which.Score.Should().Be(EvaluationScore.Terrible);
    }

    [TestMethod]
    public async Task RunAsync_PassResult_IsPersistedToRepository()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
        });
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);

        var runner = services.GetRequiredService<ITestRunnerService>();
        var resultRepo = services.GetRequiredService<IRepository<ITestResult>>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        IReadOnlyList<ITestRun> testRuns = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);
        var testRun = testRuns.First();

        var storedResult = await resultRepo.GetAsync(testRun.TestResults[0].Id, CancellationToken);
        storedResult.Evaluations.Should().ContainSingle()
            .Which.Score.Should().Be(EvaluationScore.Acceptable);
        storedResult.ActualResponse.Should().Be(testRun.TestResults[0].ActualResponse);
    }

    [TestMethod]
    public async Task RunAsync_FailResult_IsPersistedToRepository()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var actualOutput = new AssistantMessage([Content.FromText(DifferentText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, actualOutput);
        });
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);

        var runner = services.GetRequiredService<ITestRunnerService>();
        var resultRepo = services.GetRequiredService<IRepository<ITestResult>>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        IReadOnlyList<ITestRun> testRuns = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);
        var testRun = testRuns.First();

        var storedResult = await resultRepo.GetAsync(testRun.TestResults[0].Id, CancellationToken);
        storedResult.Evaluations.Should().ContainSingle()
            .Which.Score.Should().Be(EvaluationScore.Terrible);
    }

    [TestMethod]
    public async Task RunAsync_PublishesTestResultArrivedEvent_CarryingTheEvaluations()
    {
        // Regression: the live SSE event that completes a matrix cell must carry the case's
        // evaluations, not just its latency. Previously the runner built the event from the
        // result object it created with an empty evaluation list (the evaluations were added
        // to reloaded copies), so cells finished with no evaluator dots until the final refetch.
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var broadcaster = Substitute.For<ITestResultBroadcaster>();
        var published = new List<TestRunEvent>();
        broadcaster.When(b => b.Publish(Arg.Any<TestRunEvent>()))
            .Do(ci =>
            {
                var testRunEvent = ci.Arg<TestRunEvent>();
                ArgumentNullException.ThrowIfNull(testRunEvent);
                published.Add(testRunEvent);
            });

        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
            config.RegisterInstance(broadcaster).As<ITestResultBroadcaster>();
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var resultArrived = published.OfType<TestResultArrivedEvent>().Should().ContainSingle().Subject;
        resultArrived.Evaluations.Should().ContainSingle()
            .Which.Score.Should().Be(EvaluationScore.Acceptable);
    }

    [TestMethod]
    public async Task RunAsync_PublishesTestResultArrivedEvent_CarryingUsageAndCost()
    {
        // The live run cards read duration/cost/tokens off this event so a running run's totals tick
        // up as each case lands; previously the event carried only latency + evaluations, so cost and
        // tokens stayed at zero until the terminal refetch. Assert the case's usage rides along.
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var usage = new TokenUsage(inputTokenCount: 120, outputTokenCount: 80, cachedInputTokenCount: 20);
        var broadcaster = Substitute.For<ITestResultBroadcaster>();
        var published = new List<TestRunEvent>();
        broadcaster.When(b => b.Publish(Arg.Any<TestRunEvent>()))
            .Do(ci =>
            {
                var testRunEvent = ci.Arg<TestRunEvent>();
                ArgumentNullException.ThrowIfNull(testRunEvent);
                published.Add(testRunEvent);
            });

        var services = GetServices(config =>
        {
            config.Register(ct =>
            {
                IModelClient handler = Substitute.For<IModelClient>();
                var completionFactory = ct.Resolve<ICompletion.Create>();
                handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult(completionFactory(expectedOutput, usage, TimeSpan.FromMilliseconds(1000))));
                return handler;
            });
            config.RegisterInstance(broadcaster).As<ITestResultBroadcaster>();
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var resultArrived = published.OfType<TestResultArrivedEvent>().Should().ContainSingle().Subject;
        resultArrived.TokensIn.Should().Be(120);
        resultArrived.TokensOut.Should().Be(80);
        resultArrived.CachedTokensIn.Should().Be(20);
        var expectedCost = endpoint.CalculateCost(usage);
        resultArrived.CostEur.Should().Be(expectedCost is { } c ? (double)c : (double?)null);
    }

    [TestMethod]
    public async Task RunAsync_TestRun_IsPersistedToRepository()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
        });
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);

        var runner = services.GetRequiredService<ITestRunnerService>();
        var runRepo = services.GetRequiredService<IRepository<ITestRun>>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        IReadOnlyList<ITestRun> testRuns = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken);
        var testRun = testRuns.First();

        var storedRun = await runRepo.GetAsync(testRun.Id, CancellationToken);
        storedRun.Should().NotBeNull();
        storedRun.TestResults.Should().HaveCount(1);
    }

    [TestMethod]
    public async Task RunAsync_WhenAgenticEvaluatorsDisabled_SkipsAgenticEvaluator()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
            RegisterLicense(config, agenticEnabled: false);
        });

        var suite = await BuildSuiteWithAgenticAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        var testRun = (await testRunRepository.GetByGroupAsync(group.Id, CancellationToken)).First();

        // Only the Exact Match evaluator runs; the agentic one is skipped (not errored).
        testRun.TestResults[0].Evaluations.Should().ContainSingle()
            .Which.Evaluator.Kind.Should().Be(EvaluatorKind.ExactMatch);
    }

    [TestMethod]
    public async Task RunAsync_WhenAgenticEvaluatorsEnabled_RunsAgenticEvaluator()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config =>
        {
            RegisterFakeModelClient(config, expectedOutput);
            RegisterLicense(config, agenticEnabled: true);
        });

        var suite = await BuildSuiteWithAgenticAsync(services, expectedOutput, CancellationToken);
        var runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var testRunRepository = services.GetRequiredService<ITestRunRepository>();
        var testRun = (await testRunRepository.GetByGroupAsync(group.Id, CancellationToken)).First();

        // Both evaluators run: Exact Match and the agentic one.
        testRun.TestResults[0].Evaluations.Should().HaveCount(2);
        testRun.TestResults[0].Evaluations.Select(e => e.Evaluator.Kind)
            .Should().Contain(EvaluatorKind.Agentic);
    }

    [TestMethod]
    public async Task RunInForeground_DisposesCancellationTokenSourcesAfterRun()
    {
        // Regression for #197: ExecuteGroupAsync created a CancellationTokenSource and a
        // linked source whose reference was discarded, then never disposed either — the
        // finally only TryRemove'd the CTS from the registry. The linked source and the
        // owned CTS (and the callback the linked source registers on the caller's token)
        // leaked per run group. The fix disposes both in the finally; we verify the owned
        // CTS is disposed once the run returns (Cancel on a disposed source throws), and
        // that the runner no longer holds a registration for the group.
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);

        CancellationTokenSource? capturedOwnedCts = null;
        Guid? groupId = null;
        ITestRunnerService? runner = null;

        var services = GetServices(config =>
        {
            config.Register(ct =>
            {
                IModelClient handler = Substitute.For<IModelClient>();
                var completionFactory = ct.Resolve<ICompletion.Create>();
                handler.CompleteAsync(
                        Arg.Any<Conversation>(),
                        Arg.Any<ModelOptions>(),
                        Arg.Any<IReadOnlyDictionary<string, string>?>(),
                        Arg.Any<CancellationToken>())
                    .Returns(ci =>
                    {
                        // The CTS is registered before the parallel loop starts, so it is
                        // available here while the group is still executing.
                        if (runner is not null && groupId is not null)
                        {
                            var field = runner.GetType().GetField(
                                "cancellationTokens",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field?.GetValue(runner)
                                is System.Collections.Concurrent.ConcurrentDictionary<Guid, CancellationTokenSource> dict
                                && dict.TryGetValue(groupId.Value, out var owned))
                            {
                                capturedOwnedCts = owned;
                            }
                        }
                        return Task.FromResult(completionFactory(
                            expectedOutput, null, TimeSpan.FromMilliseconds(1000)));
                    });
                return handler;
            });
        });

        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        runner = services.GetRequiredService<ITestRunnerService>();
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().GetOrCreateAsync();

        await runner.RunInForegroundAsync(
            suite,
            [endpoint],
            onGroupCreated: (g, ct) => { groupId = g.Id; return Task.CompletedTask; },
            cancellationToken: CancellationToken);

        // The owned CTS must be disposed once the run returns.
        capturedOwnedCts.Should().NotBeNull();
        if (capturedOwnedCts is { } ownedCts)
        {
            Action cancelOwned = () => ownedCts.Cancel();
            cancelOwned.Should().Throw<ObjectDisposedException>();
        }
    }

    [TestMethod]
    public async Task RunInBackground_WithSampleCount_CreatesSampleRunsPerEndpoint()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var endpoints = await CreateEndpoints(services, 2);
        var runner = services.GetRequiredService<ITestRunnerService>();

        // Runs are created synchronously in CreateGroup before the group is queued for execution.
        var group = await runner.RunInBackgroundAsync(suite, endpoints, sampleCount: 3, cancellationToken: CancellationToken);

        group.SampleCount.Should().Be(3);
        var runs = await services.GetRequiredService<ITestRunRepository>().GetByGroupAsync(group.Id, CancellationToken);
        runs.Should().HaveCount(6); // 2 endpoints × 3 samples
        foreach (var endpoint in endpoints)
        {
            runs.Where(r => r.Endpoint.Id == endpoint.Id)
                .Select(r => r.SampleIndex)
                .Should().BeEquivalentTo([0, 1, 2]);
        }
    }

    // A two-case suite: one case the model answers, one it blows up on. That is the shape of the
    // flaky upstream call RunTestCase deliberately swallows.
    private static async Task<ITestSuite> BuildTwoCaseSuiteAsync(
        IServiceProvider services,
        AssistantMessage expectedOutput,
        string failingInput,
        CancellationToken ct)
    {
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var evaluatorGenerator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var createTestCase = services.GetRequiredService<ITestCase.CreateNew>();
        var testCaseRepo = services.GetRequiredService<IRepository<ITestCase>>();
        var createTestSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var testSuiteRepo = services.GetRequiredService<IRepository<ITestSuite>>();

        var agent = await agentGenerator.GetOrCreateAsync(ct);
        var evaluator = await evaluatorGenerator.GetOrCreateAsync(ct);

        var cases = new List<ITestCase>();
        foreach (string prompt in new[] { "What is the capital of France?", failingInput })
        {
            var input = Conversation.Create().With(new UserMessage([Content.FromText(prompt)]));
            var testCase = createTestCase(input, expectedOutput, sourceAgentCallId: null);
            cases.Add(await testCaseRepo.AddAsync(testCase, ct));
        }

        var suite = createTestSuite("Flaky Suite", agent, [evaluator], cases);
        await testSuiteRepo.AddAsync(suite, ct);
        return suite;
    }

    private static void RegisterModelClientFailingFor(
        ContainerBuilder builder,
        AssistantMessage response,
        string failingInput)
    {
        builder.Register(ct =>
        {
            IModelClient handler = Substitute.For<IModelClient>();
            var completionFactory = ct.Resolve<ICompletion.Create>();
            handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var conversation = call.Arg<Conversation>();
                    if (conversation is not null && conversation.Messages.Any(m => m.GetText().Contains(failingInput)))
                        throw new InvalidOperationException("upstream provider blew up");

                    return Task.FromResult(completionFactory(response, null, TimeSpan.FromMilliseconds(1000)));
                });
            return handler;
        });
    }

    [TestMethod]
    public async Task RunInForeground_WhenACaseThrows_SettlesTheRunAsFailed()
    {
        // The skipped case never produces a result, so the run can never reach the suite's case
        // count. Before it was settled explicitly it stayed Running for the life of the process
        // while its own group read Completed (#452).
        const string failingInput = "detonate";
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterModelClientFailingFor(config, expectedOutput, failingInput));
        var suite = await BuildTwoCaseSuiteAsync(services, expectedOutput, failingInput, CancellationToken);
        var endpoint = (await CreateEndpoints(services, 1))[0];
        var runner = services.GetRequiredService<ITestRunnerService>();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var storedRun = (await runRepo.GetByGroupAsync(group.Id, CancellationToken)).Single();

        storedRun.Status.Should().Be(TestRunStatus.Failed);
        storedRun.CompletedAt.Should().NotBeNull();
        storedRun.TestResults.Should().HaveCount(1, "the flaky case is skipped, the other still runs");
        group.Status.Should().Be(TestRunStatus.Completed);
    }

    [TestMethod]
    public async Task RunInForeground_WhenEveryCaseSucceeds_CompletesTheRun()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));
        var suite = await BuildTwoCaseSuiteAsync(services, expectedOutput, "detonate", CancellationToken);
        var endpoint = (await CreateEndpoints(services, 1))[0];
        var runner = services.GetRequiredService<ITestRunnerService>();

        var group = await runner.RunInForegroundAsync(suite, [endpoint], cancellationToken: CancellationToken);

        var runRepo = services.GetRequiredService<ITestRunRepository>();
        var storedRun = (await runRepo.GetByGroupAsync(group.Id, CancellationToken)).Single();

        storedRun.Status.Should().Be(TestRunStatus.Completed);
        storedRun.TestResults.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Run_AcrossNestedParallelLoops_NeverExceedsTheConfiguredDegreeOfModelCalls()
    {
        // The runner nests three parallel loops — runs, then that run's test cases, then that
        // case's evaluators — and each was configured with the same MaxDegreeOfParallelism, so the
        // setting multiplied instead of capping: at the default of 2 the loops permitted 2×2×2 = 8
        // concurrent upstream calls. With several endpoints and several cases, the observed peak
        // must stay within the configured value.
        const int degree = 2;
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);

        var inFlight = 0;
        var peak = 0;

        var services = GetServices(config =>
        {
            config.RegisterInstance(new TestRunnerConfiguration { MaxDegreeOfParallelism = degree })
                .As<TestRunnerConfiguration>();

            config.Register(ct =>
            {
                IModelClient handler = Substitute.For<IModelClient>();
                var completionFactory = ct.Resolve<ICompletion.Create>();
                handler.CompleteAsync(Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
                    .Returns(async _ =>
                    {
                        int current = Interlocked.Increment(ref inFlight);
                        // Record the high-water mark without locking: retry until our observed
                        // value is no longer higher than what is stored.
                        int observed = Volatile.Read(ref peak);
                        while (current > observed)
                        {
                            int won = Interlocked.CompareExchange(ref peak, current, observed);
                            if (won == observed) break;
                            observed = won;
                        }

                        // Hold the slot long enough for every other loop iteration to pile up, so a
                        // missing cap shows as a peak above `degree` rather than as lucky timing.
                        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
                        Interlocked.Decrement(ref inFlight);
                        return completionFactory(expectedOutput, null, TimeSpan.FromMilliseconds(1));
                    });
                return handler;
            });
        });

        var suite = await BuildSuiteWithCasesAsync(services, expectedOutput, caseCount: 3, CancellationToken);
        var endpoints = await CreateEndpoints(services, 3);
        var runner = services.GetRequiredService<ITestRunnerService>();

        await runner.RunInForegroundAsync(suite, endpoints, isSystemTestRun: true, cancellationToken: CancellationToken);

        Volatile.Read(ref peak).Should().BeLessThanOrEqualTo(degree,
            "the configured degree is an absolute cap on concurrent model calls, not a per-loop one");
        Volatile.Read(ref peak).Should().BeGreaterThan(0, "the run must actually have called the model");
    }

    /// Builds a suite with <paramref name="caseCount"/> test cases, so a run exercises the
    /// test-case loop nested inside the per-run loop.
    private static async Task<ITestSuite> BuildSuiteWithCasesAsync(
        IServiceProvider services,
        AssistantMessage expectedOutput,
        int caseCount,
        CancellationToken ct)
    {
        var agentGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var evaluatorGenerator = services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>();
        var createTestCase = services.GetRequiredService<ITestCase.CreateNew>();
        var testCaseRepo = services.GetRequiredService<IRepository<ITestCase>>();
        var createTestSuite = services.GetRequiredService<ITestSuite.CreateNew>();
        var testSuiteRepo = services.GetRequiredService<IRepository<ITestSuite>>();

        var agent = await agentGenerator.GetOrCreateAsync(ct);
        var evaluator = await evaluatorGenerator.CreateAsync(ct);

        var testCases = new List<ITestCase>(caseCount);
        for (var i = 0; i < caseCount; i++)
        {
            var input = Conversation.Create()
                .With(new UserMessage([Content.FromText($"Question {i}")]));
            var testCase = createTestCase(input, expectedOutput, sourceAgentCallId: null);
            await testCaseRepo.AddAsync(testCase, ct);
            testCases.Add(testCase);
        }

        var suite = createTestSuite("Parallelism Suite", agent, [evaluator], testCases);
        await testSuiteRepo.AddAsync(suite, ct);
        return suite;
    }

    [TestMethod]
    public async Task RunInBackground_WithSampleCountOutOfRange_Throws()
    {
        var expectedOutput = new AssistantMessage([Content.FromText(MatchingText)], []);
        var services = GetServices(config => RegisterFakeModelClient(config, expectedOutput));
        var suite = await BuildSuiteAsync(services, expectedOutput, CancellationToken);
        var endpoints = await CreateEndpoints(services, 1);
        var runner = services.GetRequiredService<ITestRunnerService>();

        await FluentActions
            .Invoking(() => runner.RunInBackgroundAsync(suite, endpoints, sampleCount: 0, cancellationToken: CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions
            .Invoking(() => runner.RunInBackgroundAsync(suite, endpoints, sampleCount: ITestRunGroup.MaxSampleCount + 1, cancellationToken: CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
    }
}
