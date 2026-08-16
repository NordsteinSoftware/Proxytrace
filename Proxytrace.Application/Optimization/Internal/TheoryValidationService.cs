using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proxytrace.Application.Optimization.Internal.Validation;
using Proxytrace.Application.Streaming;
using Nordstein.Core.Common.Async;
using Proxytrace.Domain;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.OptimizationTheory;
using Proxytrace.Domain.TestRunGroup;

namespace Proxytrace.Application.Optimization.Internal;

/// <summary>
/// Background pipeline that grounds submitted theories. Submission deduplicates and
/// rate-limits; the worker loop runs the kind-specific validator and, on a winning A/B
/// comparison, spawns a Draft <see cref="IOptimizationProposal"/>.
/// </summary>
internal sealed class TheoryValidationService : BackgroundService, ITheoryValidationService
{
    /// <summary>
    /// Number of completed test-run groups against the agent that must occur after a
    /// Rejected/Accepted decision before an identical theory is allowed to resurface.
    /// </summary>
    public const int ResurfaceThreshold = 3;

    /// <summary>
    /// Maximum number of in-flight theories (queued or validating) a single project may have at once.
    /// Because validation is serial, the queue — not the validating set — is what actually grows, so the
    /// quota is checked against the whole backlog to bound the LLM spend an open submission endpoint can trigger.
    /// </summary>
    public const int MaxInFlightPerProject = 20;

    private readonly IOptimizationTheoryRepository theories;
    private readonly IOptimizationProposalRepository proposals;
    private readonly ITestRunGroupRepository testRunGroups;
    private readonly IEnumerable<ITheoryValidator> validators;
    private readonly IProposalBroadcaster proposalBroadcaster;
    private readonly ITheoryBroadcaster theoryBroadcaster;
    private readonly ITransaction transaction;
    private readonly IAsyncLock asyncLock;
    private readonly KioskOptions kiosk;
    private readonly ILogger<TheoryValidationService> logger;
    private readonly ILogger<Audit> audit;

    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <summary>
    /// The cancellation source for each theory currently being validated, keyed by theory id. A user
    /// "Cancel validation" request cancels the matching source, which aborts the in-flight A/B run via
    /// the test runner's linked-token path. Validation is serial, so this holds at most one entry, but
    /// a dictionary keeps the lookup/lifetime explicit and race-free against <see cref="RejectAsync"/>.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> inFlightValidations = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TheoryValidationService"/> class.
    /// </summary>
    public TheoryValidationService(
        IOptimizationTheoryRepository theories,
        IOptimizationProposalRepository proposals,
        ITestRunGroupRepository testRunGroups,
        IEnumerable<ITheoryValidator> validators,
        IProposalBroadcaster proposalBroadcaster,
        ITheoryBroadcaster theoryBroadcaster,
        ITransaction transaction,
        IAsyncLock asyncLock,
        KioskOptions kiosk,
        ILogger<TheoryValidationService> logger,
        ILogger<Audit> audit)
    {
        this.theories = theories;
        this.proposals = proposals;
        this.testRunGroups = testRunGroups;
        this.validators = validators;
        this.proposalBroadcaster = proposalBroadcaster;
        this.theoryBroadcaster = theoryBroadcaster;
        this.transaction = transaction;
        this.asyncLock = asyncLock;
        this.kiosk = kiosk;
        this.logger = logger;
        this.audit = audit;
    }

    /// <summary>
    /// Submit asynchronously.
    /// </summary>
    public async Task<TheorySubmissionResult> SubmitAsync(IOptimizationTheory theory, CancellationToken cancellationToken = default)
    {
        // Serialize submissions per project: the quota and dedup checks below are check-then-act, and
        // concurrent submitters (the optimizer and the open POST /api/theories endpoint) could
        // otherwise both pass the quota/dedup gates and blow past MaxInFlightPerProject or persist
        // duplicate theories. The lock is keyed on the project so unrelated projects don't serialize.
        using IDisposable sync = await asyncLock.LockAsync(
            $"theory-submit:{theory.Agent.Project.Id}", cancellationToken);

        var inFlight = await theories.CountActiveByProjectAsync(
            theory.Agent.Project.Id, cancellationToken);
        if (inFlight >= MaxInFlightPerProject)
        {
            return new TheorySubmissionResult(TheorySubmissionOutcome.QuotaExceeded, null);
        }

        if (await ShouldSuppressAsync(theory, cancellationToken))
        {
            return new TheorySubmissionResult(TheorySubmissionOutcome.Duplicate, null);
        }

        var persisted = await theories.AddAsync(theory, cancellationToken);
        theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(persisted));
        await channel.Writer.WriteAsync(persisted.Id, cancellationToken);

        return new TheorySubmissionResult(TheorySubmissionOutcome.Accepted, persisted);
    }

    /// <summary>
    /// Reset to proposed asynchronously.
    /// </summary>
    public async Task<TheoryResetResult> ResetToProposedAsync(Guid theoryId, CancellationToken cancellationToken = default)
    {
        var theory = await theories.FindAsync(theoryId, cancellationToken);
        if (theory is null)
            return new TheoryResetResult(TheoryResetOutcome.NotFound, null);

        if (theory.Status is not (TheoryStatus.Validated or TheoryStatus.Invalidated or TheoryStatus.Failed))
            return new TheoryResetResult(TheoryResetOutcome.NotResettable, null);

        var proposalId = theory.ResultingProposalId;
        if (proposalId is { } id)
        {
            var proposal = await proposals.FindAsync(id, cancellationToken);
            if (proposal is { Status: ProposalStatus.Accepted or ProposalStatus.Adopted })
                return new TheoryResetResult(TheoryResetOutcome.BlockedByAcceptedProposal, null);
        }

        // Clear the theory's proposal reference before deleting the proposal so the two writes
        // stay consistent if interrupted; both happen in one transaction.
        var reset = await transaction.InvokeAsync(async () =>
        {
            var resetTheory = await theory.ResetToProposed(cancellationToken);
            if (proposalId is { } pid)
                await proposals.RemoveAsync(pid, cancellationToken);
            return resetTheory;
        });

        theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(reset));
        await channel.Writer.WriteAsync(reset.Id, cancellationToken);

        return new TheoryResetResult(TheoryResetOutcome.Reset, reset);
    }

    /// <summary>
    /// Rejects asynchronously.
    /// </summary>
    public async Task<TheoryRejectResult> RejectAsync(Guid theoryId, CancellationToken cancellationToken = default)
    {
        var theory = await theories.FindAsync(theoryId, cancellationToken);
        if (theory is null)
            return new TheoryRejectResult(TheoryRejectOutcome.NotFound, null);

        if (theory.Status is not (TheoryStatus.Proposed or TheoryStatus.Validating or TheoryStatus.Failed))
            return new TheoryRejectResult(TheoryRejectOutcome.NotActive, null);

        // If this theory is the one currently validating, abort its in-flight A/B run. A Proposed
        // theory still waiting in the queue has no in-flight source; the worker simply skips it
        // (the already-settled guard in ValidateAsync) when it later dequeues the now-Invalidated id.
        if (inFlightValidations.TryGetValue(theoryId, out var cts))
        {
            try
            {
                await cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Validation finished and disposed the source between the lookup and the cancel —
                // the theory is settling on its own; the transition below is harmless either way.
            }
        }

        var rejected = await theory.Reject(cancellationToken);
        theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(rejected));

        return new TheoryRejectResult(TheoryRejectOutcome.Rejected, rejected);
    }

    private async Task<bool> ShouldSuppressAsync(IOptimizationTheory theory, CancellationToken cancellationToken)
    {
        var priorTheory = await theories.FindLatestByContentHashAsync(
            theory.Agent.Id, theory.ContentHash, cancellationToken);

        // An identical theory is already pending, validating, or has already produced a proposal.
        // A Failed prior does not suppress: its validation never ran, so the idea remains untested.
        if (priorTheory is not null && priorTheory.Status is not (TheoryStatus.Invalidated or TheoryStatus.Failed))
        {
            return true;
        }

        var priorProposal = await proposals.FindLatestByContentHashAsync(
            theory.Agent.Id, theory.ContentHash, cancellationToken);

        if (priorProposal is null)
        {
            return false;
        }

        // An identical proposal is already pending review — skip the duplicate.
        if (priorProposal.Status == ProposalStatus.Draft)
        {
            return true;
        }

        // Accepted or Rejected: suppress until enough new completed groups have run since the decision.
        var completedSince = await testRunGroups.CountCompletedSinceAsync(
            theory.Agent.Id, priorProposal.UpdatedAt, cancellationToken);

        return completedSince < ResurfaceThreshold;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecoverInFlightTheoriesAsync(cancellationToken);

            await foreach (var theoryId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await ValidateAsync(theoryId, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    /// <summary>
    /// Re-queues theories that were Proposed or Validating when the process last stopped.
    /// The validation queue is in-memory, so without this the backlog would be stranded —
    /// never validated, yet permanently counted against the per-project submission quota.
    /// Skipped in kiosk mode: kiosk storage is in-memory and freshly demo-seeded on every
    /// start, so the only "backlog" recovery could find is the seeded Proposed theories —
    /// re-queuing those would fire real A/B runs (and LLM spend) on every kiosk boot when a
    /// live Kiosk:Endpoint is configured. Because of that the demo seed never leaves a theory
    /// in Validating (it would pulse "A/B in flight" forever). User-submitted theories still
    /// enqueue normally via <see cref="SubmitAsync"/>.
    /// </summary>
    internal async Task RecoverInFlightTheoriesAsync(CancellationToken cancellationToken)
    {
        if (kiosk.Enabled)
            return;

        try
        {
            var active = await theories.GetActiveAsync(cancellationToken);
            foreach (var theory in active)
            {
                await channel.Writer.WriteAsync(theory.Id, cancellationToken);
            }

            if (active.Count > 0)
                logger.LogInformation("Re-queued {Count} in-flight theory/theories after restart", active.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to recover in-flight theories after restart");
        }
    }

    internal async Task ValidateAsync(Guid theoryId, CancellationToken cancellationToken)
    {
        IOptimizationTheory? theory = await theories.FindAsync(theoryId, cancellationToken);
        if (theory is null)
        {
            logger.LogWarning("Theory {TheoryId} not found — skipping validation", theoryId);
            return;
        }

        if (theory.Status is TheoryStatus.Validated or TheoryStatus.Invalidated or TheoryStatus.Failed)
        {
            // Already settled — e.g. a theory submitted while restart recovery was re-queuing
            // the backlog can be enqueued twice.
            return;
        }

        // Register a per-theory cancellation source so a user "Cancel validation" request (RejectAsync)
        // can abort this in-flight A/B run. Linked to the service stopping token so shutdown still cancels.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inFlightValidations[theoryId] = cts;
        try
        {
            // A recovered theory may already be Validating from before the restart.
            if (theory.Status == TheoryStatus.Proposed)
            {
                theory = await theory.SetValidating(cts.Token);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
            }

            // Link the candidate A/B run to the theory the moment it is created — while still
            // Validating — so reviewers can watch the in-flight run, not just the finished one.
            async Task OnCandidateRun(Guid candidateRunId, CancellationToken ct)
            {
                if (theory is null)
                    return;
                theory = await theory.AttachAbTestRun(candidateRunId, ct);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
            }

            var validator = validators.FirstOrDefault(v => v.CanValidate(theory));
            TheoryValidationOutcome outcome = validator is null
                ? TheoryValidationOutcome.CouldNotTest
                : await validator.ValidateAsync(theory, cts.Token, OnCandidateRun);

            if (outcome.Proposal is { } proposal)
            {
                // Persist the proposal and mark the theory validated atomically — a crash between
                // the two writes would otherwise leave an orphaned Draft proposal that no theory
                // references. Broadcast only after the transaction commits.
                var (persisted, validated) = await transaction.InvokeAsync(async () =>
                {
                    var savedProposal = await proposals.AddAsync(proposal, cancellationToken);
                    var savedTheory = await theory.SetValidated(
                        savedProposal.Id, outcome.BaselinePassRate, outcome.ProjectedPassRate, outcome.PValue,
                        outcome.CandidateRunId ?? theory.ABTestRunId, cancellationToken);
                    return (savedProposal, savedTheory);
                });

                proposalBroadcaster.Publish(ProposalCreatedEvent.Create(persisted));
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(validated));
                audit.LogAudit(
                    AuditAction.TheoryValidated, nameof(IOptimizationTheory), validated.Id, validated.Agent.Name,
                    projectId: validated.Agent.Project.Id,
                    details: JsonSerializer.Serialize(new
                    {
                        baselinePassRate = outcome.BaselinePassRate,
                        projectedPassRate = outcome.ProjectedPassRate,
                        pValue = outcome.PValue,
                    }));
                audit.LogAudit(
                    AuditAction.ProposalGenerated, nameof(IOptimizationProposal), persisted.Id, validated.Agent.Name,
                    projectId: validated.Agent.Project.Id);
                logger.LogInformation("Theory {TheoryId} validated; produced proposal {ProposalId}", theoryId, persisted.Id);
            }
            else if (outcome.NotTested)
            {
                // The A/B comparison never happened (unreachable provider, incomplete run, no
                // validator) — the theory is unproven, not disproven. Settle it Failed so it stays
                // out of the win-rate statistics and can be retried.
                theory = await theory.SetFailed(outcome.CandidateRunId ?? theory.ABTestRunId, cancellationToken);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
                audit.LogAudit(
                    AuditAction.TheoryValidationFailed, nameof(IOptimizationTheory), theory.Id, theory.Agent.Name,
                    projectId: theory.Agent.Project.Id);
                logger.LogWarning("Theory {TheoryId} could not be validated — A/B run incomplete", theoryId);
            }
            else
            {
                // Rejected outcomes carry the candidate run id, but the observer may already have
                // linked the run while validating — never downgrade a known link back to null.
                theory = await theory.SetInvalidated(
                    outcome.BaselinePassRate, outcome.ProjectedPassRate, outcome.PValue,
                    outcome.CandidateRunId ?? theory.ABTestRunId, cancellationToken);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
                audit.LogAudit(
                    AuditAction.TheoryInvalidated, nameof(IOptimizationTheory), theory.Id, theory.Agent.Name,
                    projectId: theory.Agent.Project.Id,
                    details: JsonSerializer.Serialize(new
                    {
                        baselinePassRate = outcome.BaselinePassRate,
                        projectedPassRate = outcome.ProjectedPassRate,
                        pValue = outcome.PValue,
                    }));
                logger.LogInformation("Theory {TheoryId} invalidated — no improvement", theoryId);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A user cancelled this theory's validation: the in-flight run was aborted. RejectAsync
            // normally transitions the theory to Invalidated first, but if that write lost an optimistic
            // concurrency race (or the cancellation came from elsewhere) the theory could still be
            // Validating — settle it. TryInvalidateAsync is a no-op when it is already terminal, so this
            // does not double-broadcast in the normal path.
            await TryInvalidateAsync(theoryId, cancellationToken);
        }
        catch (Exception ex)
        {
            // An errored validation is not a disproven theory: settle it Failed (retryable,
            // excluded from win-rate stats) instead of Invalidated.
            logger.LogWarning(ex, "Validation failed for theory {TheoryId}", theoryId);
            await TryFailAsync(theoryId, cancellationToken);
        }
        finally
        {
            inFlightValidations.TryRemove(theoryId, out _);
        }
    }

    private async Task TryInvalidateAsync(Guid theoryId, CancellationToken cancellationToken)
    {
        try
        {
            var theory = await theories.FindAsync(theoryId, cancellationToken);
            if (theory is { Status: TheoryStatus.Validating })
            {
                // Preserve any A/B run already linked while validating; only the metrics are unknown.
                theory = await theory.SetInvalidated(null, null, null, theory.ABTestRunId, cancellationToken);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark theory {TheoryId} as invalidated after error", theoryId);
        }
    }

    private async Task TryFailAsync(Guid theoryId, CancellationToken cancellationToken)
    {
        try
        {
            var theory = await theories.FindAsync(theoryId, cancellationToken);
            if (theory is { Status: TheoryStatus.Validating })
            {
                // Preserve any A/B run already linked while validating so the failure can be diagnosed.
                theory = await theory.SetFailed(theory.ABTestRunId, cancellationToken);
                theoryBroadcaster.Publish(TheoryStatusChangedEvent.Create(theory));
                audit.LogAudit(
                    AuditAction.TheoryValidationFailed, nameof(IOptimizationTheory), theory.Id, theory.Agent.Name,
                    projectId: theory.Agent.Project.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark theory {TheoryId} as failed after error", theoryId);
        }
    }
}
