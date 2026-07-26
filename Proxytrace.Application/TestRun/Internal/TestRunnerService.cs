using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Anomaly;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Streaming;
using Proxytrace.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;

namespace Proxytrace.Application.TestRun.Internal;

internal class TestRunnerService : BackgroundService, ITestRunnerService
{
    private readonly ITestResult.CreateNew createTestResult;
    private readonly ITestRun.CreateNew createTestRun;
    private readonly ITestRunGroup.CreateNew createTestRunGroup;
    private readonly IEvaluation.CreateErrored createErroredEvaluation;
    private readonly ITestRunRepository testRunRepository;
    private readonly ITestRunGroupRepository testRunGroupRepository;
    private readonly IRepository<ITestResult> testResultRepository;
    private readonly ITestResultBroadcaster broadcaster;
    private readonly IOptimizerService optimizer;
    private readonly IAnomalyDetectionService anomalyDetection;
    private readonly ILicenseService license;
    private readonly IAsyncLock asyncLock;
    private readonly ILogger<TestRunnerService> logger;
    private readonly TestRunnerConfiguration configuration;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> cancellationTokens = new();

    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public TestRunnerService(
        ITestResult.CreateNew createTestResult,
        ITestRun.CreateNew createTestRun,
        ITestRunGroup.CreateNew createTestRunGroup,
        IEvaluation.CreateErrored createErroredEvaluation,
        ITestRunRepository testRunRepository,
        ITestRunGroupRepository testRunGroupRepository,
        IRepository<ITestResult> testResultRepository,
        ITestResultBroadcaster broadcaster,
        IOptimizerService optimizer,
        IAnomalyDetectionService anomalyDetection,
        ILicenseService license,
        IAsyncLock asyncLock,
        ILogger<TestRunnerService> logger,
        TestRunnerConfiguration configuration)
    {
        this.createTestResult = createTestResult;
        this.createTestRun = createTestRun;
        this.createTestRunGroup = createTestRunGroup;
        this.createErroredEvaluation = createErroredEvaluation;
        this.testRunRepository = testRunRepository;
        this.testRunGroupRepository = testRunGroupRepository;
        this.testResultRepository = testResultRepository;
        this.broadcaster = broadcaster;
        this.optimizer = optimizer;
        this.anomalyDetection = anomalyDetection;
        this.license = license;
        this.asyncLock = asyncLock;
        this.logger = logger;
        this.configuration = configuration;
    }

    public async Task<ITestRunGroup> RunInForegroundAsync(
        ITestSuite suite,
        IReadOnlyList<IModelEndpoint> endpoints,
        IAgent? customAgent = null,
        bool isSystemTestRun = false,
        Func<ITestRunGroup, CancellationToken, Task>? onGroupCreated = null,
        int sampleCount = 1,
        CancellationToken cancellationToken = default)
    {
        ITestRunGroup group = await CreateGroup(suite, endpoints, isSystemTestRun, scheduleId: null, sampleCount, cancellationToken);
        if (onGroupCreated is not null)
            await onGroupCreated(group, cancellationToken);
        return await ExecuteGroupAsync(group, customAgent, isSystemTestRun, cancellationToken);
    }

    public async Task<ITestRunGroup> RunInBackgroundAsync(
        ITestSuite suite,
        IReadOnlyList<IModelEndpoint> endpoints,
        Guid? scheduleId = null,
        int sampleCount = 1,
        CancellationToken cancellationToken = default)
    {
        ITestRunGroup group = await CreateGroup(suite, endpoints, isSystemRun: false, scheduleId, sampleCount, cancellationToken);
        await channel.Writer.WriteAsync(group.Id, cancellationToken);
        return group;
    }

    private async Task<ITestRunGroup> CreateGroup(
        ITestSuite suite,
        IReadOnlyList<IModelEndpoint> endpoints,
        bool isSystemRun,
        Guid? scheduleId,
        int sampleCount,
        CancellationToken cancellationToken)
    {
        if (endpoints.Count > ITestRunGroup.MaxModelEndpoints)
            throw new ArgumentException(
                $"A test suite can be run against at most {ITestRunGroup.MaxModelEndpoints} model endpoints.",
                nameof(endpoints));

        if (sampleCount is < 1 or > ITestRunGroup.MaxSampleCount)
            throw new ArgumentException(
                $"Sample count must be between 1 and {ITestRunGroup.MaxSampleCount}.",
                nameof(sampleCount));

        ITestRunGroup group = createTestRunGroup(suite, isSystemRun, scheduleId, sampleCount);
        group = await testRunGroupRepository.AddAsync(group, cancellationToken);

        // One run per (endpoint, sample). All runs in a group share the suite; runs sharing an
        // endpoint form a "cohort" that the UI averages and the optimization loop reduces to one
        // representative run.
        foreach (var endpoint in endpoints)
        {
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                ITestRun newRun = createTestRun(group, endpoint, sampleIndex);
                await testRunRepository.AddAsync(newRun, cancellationToken);
            }
        }

        return group;
    }

    public async Task<ITestRunGroup> CancelAsync(ITestRunGroup group, CancellationToken cancellationToken = default)
    {
        var runs = await testRunRepository.GetByGroupAsync(group.Id, cancellationToken);

        foreach (var run in runs)
        {
            if (cancellationTokens.TryGetValue(run.Id, out CancellationTokenSource? cts))
                await cts.CancelAsync();
        }

        // Reload runs and mark any that didn't reach a terminal state as Cancelled.
        var reloaded = await testRunRepository.GetByGroupAsync(group.Id, cancellationToken);
        foreach (var run in reloaded.Where(r => !r.Status.IsTerminal()))
            await run.SetCancelled(cancellationToken);

        group = await testRunGroupRepository.GetAsync(group.Id, cancellationToken);
        if (!group.Status.IsTerminal())
        {
            group = await group.SetCancelled(cancellationToken);
            broadcaster.PublishGroupComplete(GroupRunCompleteEvent.Create(group));
        }

        return group;
    }

    private async Task<ITestRunGroup> ExecuteGroupAsync(
        ITestRunGroup group,
        IAgent? customAgent = null,
        bool isSystemTestRun = false,
        CancellationToken cancellationToken = default)
    {
        if (group.Status != TestRunStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot execute test run {group.Id} because it is not in pending status.");
        }
        await group.SetRunning(cancellationToken);

        CancellationTokenSource cts = new CancellationTokenSource();
        cancellationTokens.TryAdd(group.Id, cts);
        CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        cancellationToken = linkedCts.Token;

        try
        {
            var testRuns = await testRunRepository.GetByGroupAsync(group.Id, cancellationToken);
            
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = configuration.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };
            
            await Parallel.ForEachAsync(
                testRuns,
                parallelOptions,
                async (testRun, ct) => await RunTestRun(testRun, customAgent, ct));

            group = await group.ReloadAsync(cancellationToken);
            group = await group.SetCompleted(cancellationToken);
            broadcaster.PublishGroupComplete(GroupRunCompleteEvent.Create(group));

            if (!isSystemTestRun)
            {
                await optimizer.EnqueueAsync(group, cancellationToken);
                await anomalyDetection.EnqueueAsync(group, cancellationToken);
            }
            return group;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The run was cancelled (process shutdown, or a user cancelling theory validation through
            // the linked token). Mark the group and any non-terminal runs Cancelled with a fresh token
            // so the in-flight A/B run isn't stranded in Running forever, then rethrow. Best-effort.
            try
            {
                var runs = await testRunRepository.GetByGroupAsync(group.Id, CancellationToken.None);
                foreach (var run in runs.Where(r => !r.Status.IsTerminal()))
                    await run.SetCancelled(CancellationToken.None);

                group = await group.ReloadAsync(CancellationToken.None);
                if (!group.Status.IsTerminal())
                {
                    group = await group.SetCancelled(CancellationToken.None);
                    broadcaster.PublishGroupComplete(GroupRunCompleteEvent.Create(group));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to mark cancelled test run group {GroupId} terminal", group.Id);
            }

            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Test run group {GroupId} failed", group.Id);
            try
            {
                group = await group.ReloadAsync(CancellationToken.None);
                if (!group.Status.IsTerminal())
                {
                    group = await group.SetFailed(CancellationToken.None);
                }
                broadcaster.PublishGroupComplete(GroupRunCompleteEvent.Create(group));

                // A failed group is the most important anomaly. The success-path enqueue above is
                // skipped when we land here, so detect from the failure path too.
                if (!isSystemTestRun)
                {
                    await anomalyDetection.EnqueueAsync(group, CancellationToken.None);
                }
            }
            catch (Exception broadcastEx)
            {
                logger.LogError(broadcastEx, "Failed to mark test run group {GroupId} as Failed", group.Id);
            }
            return group;
        }
        finally
        {
            cancellationTokens.TryRemove(group.Id, out _);
            linkedCts.Dispose();
            cts.Dispose();
        }
    }

    private async Task RunTestRun(
        ITestRun testRun,
        IAgent? customAgent,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = configuration.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            testRun.Group.Suite.TestCases,
            parallelOptions,
            async (testCase, ct) => await RunTestCase(testCase, testRun, customAgent, ct));

        testRun = await testRun.ReloadAsync(cancellationToken);
        broadcaster.PublishComplete(RunCompleteEvent.Create(testRun));
    }

    private async Task RunTestCase(
        ITestCase testCase,
        ITestRun testRun,
        IAgent? customAgent,
        CancellationToken cancellationToken)
    {
        broadcaster.Publish(new TestCaseStartedEvent(testRun.Id, testRun.Group.Id, testCase.Id));

        try
        {
            IAgent agent = customAgent ?? testRun.Group.Suite.Agent;
            using IModelClient client = agent.CreateClient(
                customEndpoint: testRun.Endpoint,
                skipIngestion: true);
            ICompletion completion = await client.CompleteAsync(
                testCase.Input,
                cancellationToken: cancellationToken);

            broadcaster.Publish(new InferenceDoneEvent(testRun.Id, testRun.Group.Id, testCase.Id));

            var testResult = createTestResult(testCase, completion, []);
            await testResultRepository.AddAsync(testResult, cancellationToken);

            var run = testRun;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = configuration.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            };

            // Agentic evaluators require the AgenticEvaluators license feature. On unlicensed installs
            // they are skipped (not run, no evaluation produced) rather than errored — the pass rate is
            // computed over judged evaluators. The suite editor mirrors this by locking agentic
            // evaluators in the UI; an evaluator attached while licensed simply won't run after a
            // downgrade.
            var agenticEnabled = license.IsFeatureEnabled(LicenseFeature.AgenticEvaluators);
            var evaluators = testRun.Group.Suite.Evaluators
                .Where(e => agenticEnabled || e.Kind != EvaluatorKind.Agentic);

            await Parallel.ForEachAsync(evaluators, parallelOptions,
                async (evaluator, ct) => await RunEvaluator(evaluator, testResult, run, ct));

            using var sync = await asyncLock.LockAsync(testRun.Id, cancellationToken);
            testRun = await testRun.ReloadAsync(cancellationToken);
            testRun = await testRun.SetTestResult(testResult, cancellationToken);

            // Reload the result before broadcasting: the evaluations were added to reloaded copies
            // inside RunEvaluator, so this local reference still holds the empty list it was created
            // with. Without the reload the completing SSE event carries no evaluations and a finished
            // matrix cell shows no evaluator dots until the terminal group refetch.
            testResult = await testResult.ReloadAsync(cancellationToken);
            broadcaster.Publish(TestResultArrivedEvent.Create(testRun, testResult));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Run was cancelled — let it unwind so the group is marked Cancelled, not Failed.
            throw;
        }
        catch (Exception ex)
        {
            // A single case's inference/evaluation failure (flaky LLM call, transient timeout) must
            // not abort the whole run: log it and skip this case so the remaining cases still run and
            // the group completes. Validation guards against scoring an incomplete run (see the A/B
            // validators' result-count check).
            logger.LogError(ex,
                "Test case {TestCaseId} in run {RunId} failed; skipping it and continuing the run",
                testCase.Id, testRun.Id);
        }
    }

    private async Task RunEvaluator(
        IEvaluator evaluator,
        ITestResult testResult,
        ITestRun testRun,
        CancellationToken cancellationToken)
    {
        IEvaluation? evaluation;
        var sw = Stopwatch.StartNew();
        try
        {
            evaluation = await evaluator.EvaluateAsync(testResult, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Evaluator {EvaluatorId} ({EvaluatorKind}) failed for test result {TestResultId}",
                evaluator.Id,
                evaluator.Kind,
                testResult.Id);
            evaluation = createErroredEvaluation(evaluator, sw.Elapsed, ex);
        }

        if (evaluation is null)
        {
            return;
        }

        using var sync = await asyncLock.LockAsync(testResult.Id, cancellationToken);
        testResult = await testResult.ReloadAsync(cancellationToken);
        await testResult.AddEvaluationAsync(evaluation, cancellationToken);
        broadcaster.Publish(new EvaluationArrivedEvent(
            testRun.Id,
            testRun.Group.Id,
            testResult.TestCase.Id,
            new EvaluationEventData(
                evaluator.Id,
                evaluator.Kind,
                evaluator.Name,
                evaluation.Score,
                evaluation.Reasoning,
                evaluation.ErrorMessage)));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (Guid groupId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    ITestRunGroup? group = await testRunGroupRepository.FindAsync(groupId, cancellationToken);
                    if (group != null)
                    {
                        await ExecuteGroupAsync(group, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        logger.LogWarning("Test run group with ID {RunId} not found in repository", groupId);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // user-initiated cancellation — not an error
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to execute test run {RunId}", groupId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }
}
