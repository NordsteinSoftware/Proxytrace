using Proxytrace.Domain.Statistics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Application.Statistics.Internal.Worker;

internal class StatisticsBackfillHostedService : IHostedService
{
    private static readonly TestRunStatus[] TerminalStatuses =
        [TestRunStatus.Completed, TestRunStatus.Failed, TestRunStatus.Cancelled];

    private readonly ITestRunRepository testRuns;
    private readonly IStatsReader<TestRunStats, TestRunStats.Filter> runStatsReader;
    private readonly IEnumerable<IStatsProjector> projectors;
    private readonly ILogger<StatisticsBackfillHostedService> logger;

    private CancellationTokenSource? cts;
    private Task? backfillTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatisticsBackfillHostedService"/> class.
    /// </summary>
    public StatisticsBackfillHostedService(
        ITestRunRepository testRuns,
        IStatsReader<TestRunStats, TestRunStats.Filter> runStatsReader,
        IEnumerable<IStatsProjector> projectors,
        ILogger<StatisticsBackfillHostedService> logger)
    {
        this.testRuns = testRuns;
        this.runStatsReader = runStatsReader;
        this.projectors = projectors;
        this.logger = logger;
    }

    /// <summary>
    /// Starts asynchronously.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        backfillTask = Task.Run(() => RunAsync(cts.Token), cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops asynchronously.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (backfillTask is null)
        {
            return;
        }

        try
        {
            await backfillTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (cts is not null)
            {
                await cts.CancelAsync();
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        IStatsProjector[] testRunProjectors = projectors
            .Where(p => p.EntityType == typeof(ITestRun))
            .ToArray();

        if (testRunProjectors.Length == 0)
        {
            return;
        }

        try
        {
            IReadOnlyList<ITestRun> runs = await testRuns.GetByStatusAsync(TerminalStatuses, cancellationToken);
            int projected = 0;

            foreach (ITestRun run in runs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Skip runs already projected — avoids racing the live drainer when a run finalizes
                // during backfill, and is a cheap no-op on warm restarts.
                //
                // System runs invert the test. They must NOT appear in the statistics, so the work
                // here is the opposite: a system run with a leftover row (written before that rule
                // existed) needs projecting precisely so the projector can remove it, while one with
                // no row is already correct and is skipped.
                TestRunStats? existing = await runStatsReader.FindAsync(run.Id, cancellationToken);
                bool isSystemRun = run.Group.IsSystemRun;
                if (isSystemRun ? existing is null : existing is not null)
                {
                    continue;
                }

                foreach (IStatsProjector projector in testRunProjectors)
                {
                    try
                    {
                        await projector.ProjectAsync(run.Id, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Backfill projector {Projector} failed for run {RunId}", projector.GetType().Name, run.Id);
                    }
                }

                projected++;
            }

            logger.LogInformation("Statistics backfill scanned {Total} runs, projected {Projected} missing.", runs.Count, projected);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Statistics backfill failed");
        }
    }
}
