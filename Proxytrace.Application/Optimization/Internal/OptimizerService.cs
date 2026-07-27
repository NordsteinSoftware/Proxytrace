using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization.Internal;

internal class OptimizerService : BackgroundService, IOptimizerService
{
    private readonly IOptimizer optimizer;
    private readonly ITestRunGroupRepository testRunGroupRepository;
    private readonly ITheoryValidationService theoryValidationService;
    private readonly KioskOptions kiosk;
    private readonly ILogger<OptimizerService> logger;

    /// <summary>
    /// Upper bound on how many groups one restart re-queues, so a long-dormant install does not
    /// enqueue its whole history — and its whole LLM cost — on the first start after upgrading.
    /// The remainder is picked up on the next start.
    /// </summary>
    private const int MaxRecoveredGroups = 50;

    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public OptimizerService(
        IOptimizer optimizer,
        ITestRunGroupRepository testRunGroupRepository,
        ITheoryValidationService theoryValidationService,
        KioskOptions kiosk,
        ILogger<OptimizerService> logger)
    {
        this.optimizer = optimizer;
        this.testRunGroupRepository = testRunGroupRepository;
        this.theoryValidationService = theoryValidationService;
        this.kiosk = kiosk;
        this.logger = logger;
    }

    public Task EnqueueAsync(ITestRunGroup testRunGroup, CancellationToken cancellationToken = default)
        => channel.Writer.WriteAsync(testRunGroup.Id, cancellationToken).AsTask();

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecoverPendingGroupsAsync(cancellationToken);

            await foreach (var groupId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var group = await testRunGroupRepository.FindAsync(groupId, cancellationToken);
                    if (group is null)
                    {
                        logger.LogWarning("Test run group {GroupId} not found — skipping optimization", groupId);
                        continue;
                    }

                    if (group.OptimizationConsideredAt is not null)
                    {
                        // Already handled — a group submitted while restart recovery was re-queuing
                        // the backlog can be enqueued twice. Mirrors the theory queue's own check.
                        continue;
                    }

                    logger.LogInformation("Discovering optimization theories for test run group {GroupId}", groupId);
                    var theories = await optimizer.DiscoverTheories(group, cancellationToken);

                    var submitted = 0;
                    foreach (var theory in theories)
                    {
                        var result = await theoryValidationService.SubmitAsync(theory, cancellationToken);
                        if (result.Outcome == TheorySubmissionOutcome.Accepted)
                            submitted++;
                    }

                    // Marked only after the theories have been submitted, so a crash mid-discovery
                    // leaves the group pending and it is retried on the next start. Marked even when
                    // discovery produced nothing: "considered and found nothing" must not be
                    // mistaken for "never considered", or every barren group would be reprocessed
                    // on every boot forever.
                    await group.MarkOptimizationConsidered(cancellationToken);

                    logger.LogInformation(
                        "Group {GroupId} produced {Discovered} theory/theories, {Submitted} submitted for validation",
                        groupId, theories.Count, submitted);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // individual job cancelled — continue processing
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Optimization failed for test run group {GroupId}", groupId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    /// <summary>
    /// Re-queues completed groups that were never considered — the backlog a restart would otherwise
    /// discard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The optimizer queue is an in-memory channel, so anything still in it when the process stopped
    /// was simply lost: a deploy during a scheduled-run window silently dropped that night's
    /// optimization, with nothing recorded anywhere to show it had happened. The theory queue has
    /// had this recovery all along; it could, because a theory's status is its own durable marker.
    /// A group had no equivalent until <see cref="ITestRunGroup.OptimizationConsideredAt"/>.
    /// </para>
    /// <para>
    /// Skipped in kiosk mode for the same reason the theory queue skips it: kiosk storage is
    /// in-memory and freshly demo-seeded on every start, so the only backlog recovery could find is
    /// the seeded groups — re-queuing those would fire real A/B runs, and real spend, on every boot
    /// when a live endpoint is configured.
    /// </para>
    /// </remarks>
    internal async Task RecoverPendingGroupsAsync(CancellationToken cancellationToken)
    {
        if (kiosk.Enabled)
            return;

        try
        {
            var pending = await testRunGroupRepository.GetPendingOptimizationAsync(
                MaxRecoveredGroups, cancellationToken);

            foreach (var group in pending)
            {
                await channel.Writer.WriteAsync(group.Id, cancellationToken);
            }

            if (pending.Count > 0)
                logger.LogInformation("Re-queued {Count} test run group(s) for optimization after restart", pending.Count);

            if (pending.Count == MaxRecoveredGroups)
            {
                // Never truncate silently: the remainder is picked up on the next start, but an
                // operator seeing optimization lag deserves to know a cap is in play.
                logger.LogWarning(
                    "Optimization recovery hit its {Cap}-group cap; the remaining backlog is deferred to the next start.",
                    MaxRecoveredGroups);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to recover pending optimization groups after restart");
        }
    }
}
