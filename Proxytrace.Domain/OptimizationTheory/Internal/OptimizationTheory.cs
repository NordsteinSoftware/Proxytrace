using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Nordstein.Core.Domain;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory.Internal;

/// <summary>
/// Shared base for the concrete theory kinds. Holds the common hypothesis metadata
/// and the lifecycle state machine; each subtype contributes the proposed-change payload.
/// </summary>
internal abstract record OptimizationTheory : DomainEntity<IOptimizationTheory>, IOptimizationTheory
{
    /// <summary>
    /// Gets or sets the agent.
    /// </summary>
    public IAgent Agent { get; private init; }
    /// <summary>
    /// Gets or sets the suite.
    /// </summary>
    public ITestSuite Suite { get; private init; }
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public abstract ProposalKind Kind { get; }
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public TheoryStatus Status { get; private init; }
    /// <summary>
    /// Gets or sets the source.
    /// </summary>
    public TheorySource Source { get; private init; }
    /// <summary>
    /// Gets or sets the priority.
    /// </summary>
    public Priority Priority { get; private init; }
    /// <summary>
    /// Gets or sets the rationale.
    /// </summary>
    public string Rationale { get; private init; }
    /// <summary>
    /// Gets or sets the evidence test run ids.
    /// </summary>
    public IReadOnlyCollection<Guid> EvidenceTestRunIds { get; private init; }
    /// <summary>
    /// Gets or sets the resulting proposal id.
    /// </summary>
    public Guid? ResultingProposalId { get; private init; }
    /// <summary>
    /// Gets or sets the baseline pass rate.
    /// </summary>
    public double? BaselinePassRate { get; private init; }
    /// <summary>
    /// Gets or sets the projected pass rate.
    /// </summary>
    public double? ProjectedPassRate { get; private init; }
    /// <summary>
    /// Gets or sets the p value.
    /// </summary>
    public double? PValue { get; private init; }
    /// <summary>
    /// Gets or sets the ab test run id.
    /// </summary>
    public Guid? ABTestRunId { get; private init; }
    /// <summary>
    /// Gets or sets the content hash.
    /// </summary>
    public string ContentHash { get; private init; }

    protected OptimizationTheory(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        string contentHash,
        IRepository<IOptimizationTheory> repository) : base(repository)
    {
        Agent = agent;
        Suite = suite;
        Status = TheoryStatus.Proposed;
        Source = source;
        Priority = priority;
        Rationale = rationale;
        EvidenceTestRunIds = evidenceTestRunIds.ToArray();
        ResultingProposalId = null;
        ContentHash = contentHash;
    }

    protected OptimizationTheory(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing,
        IRepository<IOptimizationTheory> repository) : base(existing, repository)
    {
        Agent = agent;
        Suite = suite;
        Status = status;
        Source = source;
        Priority = priority;
        Rationale = rationale;
        EvidenceTestRunIds = evidenceTestRunIds.ToArray();
        ResultingProposalId = resultingProposalId;
        BaselinePassRate = baselinePassRate;
        ProjectedPassRate = projectedPassRate;
        PValue = pValue;
        ABTestRunId = abTestRunId;
        ContentHash = contentHash;
    }

    /// <summary>
    /// Sets the validating.
    /// </summary>
    public Task<IOptimizationTheory> SetValidating(CancellationToken cancellationToken = default)
    {
        if (Status != TheoryStatus.Proposed)
            throw new InvalidOperationException($"Cannot start validating theory {Id} from status {Status}.");

        return ApplyAsync(this with { Status = TheoryStatus.Validating }, cancellationToken);
    }

    /// <summary>
    /// Attaches the ab test run.
    /// </summary>
    public Task<IOptimizationTheory> AttachAbTestRun(Guid abTestRunId, CancellationToken cancellationToken = default)
    {
        if (Status != TheoryStatus.Validating)
            throw new InvalidOperationException($"Cannot attach an A/B run to theory {Id} from status {Status}.");

        return ApplyAsync(this with { ABTestRunId = abTestRunId }, cancellationToken);
    }

    /// <summary>
    /// Sets the validated.
    /// </summary>
    public Task<IOptimizationTheory> SetValidated(
        Guid resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        CancellationToken cancellationToken = default)
    {
        if (Status != TheoryStatus.Validating)
            throw new InvalidOperationException($"Cannot validate theory {Id} from status {Status}.");

        return ApplyAsync(
            this with
            {
                Status = TheoryStatus.Validated,
                ResultingProposalId = resultingProposalId,
                BaselinePassRate = baselinePassRate,
                ProjectedPassRate = projectedPassRate,
                PValue = pValue,
                ABTestRunId = abTestRunId,
            },
            cancellationToken);
    }

    /// <summary>
    /// Sets the invalidated.
    /// </summary>
    public Task<IOptimizationTheory> SetInvalidated(
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        CancellationToken cancellationToken = default)
    {
        if (Status != TheoryStatus.Validating)
            throw new InvalidOperationException($"Cannot invalidate theory {Id} from status {Status}.");

        return ApplyAsync(
            this with
            {
                Status = TheoryStatus.Invalidated,
                BaselinePassRate = baselinePassRate,
                ProjectedPassRate = projectedPassRate,
                PValue = pValue,
                ABTestRunId = abTestRunId,
            },
            cancellationToken);
    }

    /// <summary>
    /// Sets the failed.
    /// </summary>
    public Task<IOptimizationTheory> SetFailed(Guid? abTestRunId, CancellationToken cancellationToken = default)
    {
        if (Status != TheoryStatus.Validating)
            throw new InvalidOperationException($"Cannot fail theory {Id} from status {Status}.");

        // No metrics: the A/B comparison never completed, so nothing was measured. The run link is
        // kept (when known) so the user can inspect what went wrong.
        return ApplyAsync(this with { Status = TheoryStatus.Failed, ABTestRunId = abTestRunId }, cancellationToken);
    }

    /// <summary>
    /// Rejects.
    /// </summary>
    public Task<IOptimizationTheory> Reject(CancellationToken cancellationToken = default)
    {
        if (Status is not (TheoryStatus.Proposed or TheoryStatus.Validating or TheoryStatus.Failed))
            throw new InvalidOperationException($"Cannot reject theory {Id} from status {Status}.");

        // Keep any A/B run already linked while validating for provenance; record no metrics so a
        // manual dismissal stays distinguishable from an A/B-disproven invalidation.
        return ApplyAsync(this with { Status = TheoryStatus.Invalidated }, cancellationToken);
    }

    /// <summary>
    /// Reset to proposed.
    /// </summary>
    public Task<IOptimizationTheory> ResetToProposed(CancellationToken cancellationToken = default)
    {
        if (Status is not (TheoryStatus.Validated or TheoryStatus.Invalidated or TheoryStatus.Failed))
            throw new InvalidOperationException($"Cannot reset theory {Id} to Proposed from status {Status}.");

        return ApplyAsync(
            this with
            {
                Status = TheoryStatus.Proposed,
                ResultingProposalId = null,
                BaselinePassRate = null,
                ProjectedPassRate = null,
                PValue = null,
                ABTestRunId = null,
            },
            cancellationToken);
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Agent.Validate(validationContext))
            yield return result;

        foreach (var result in Suite.Validate(validationContext))
            yield return result;

        if (string.IsNullOrWhiteSpace(Rationale))
            yield return Validation.NotNullOrWhiteSpace(Rationale);

        if (string.IsNullOrWhiteSpace(ContentHash))
            yield return Validation.NotNullOrWhiteSpace(ContentHash);

        if (BaselinePassRate is { } baselinePassRate &&
            (!double.IsFinite(baselinePassRate) || baselinePassRate is < 0 or > 1))
            yield return new ValidationResult(
                $"{nameof(BaselinePassRate)} must be a finite value between 0 and 1.",
                [nameof(BaselinePassRate)]);

        if (ProjectedPassRate is { } projectedPassRate &&
            (!double.IsFinite(projectedPassRate) || projectedPassRate is < 0 or > 1))
            yield return new ValidationResult(
                $"{nameof(ProjectedPassRate)} must be a finite value between 0 and 1.",
                [nameof(ProjectedPassRate)]);

        // A p-value outside [0, 1] is statistically meaningless.
        if (PValue is { } pValue &&
            (!double.IsFinite(pValue) || pValue is < 0 or > 1))
            yield return new ValidationResult(
                $"{nameof(PValue)} must be a finite value between 0 and 1.",
                [nameof(PValue)]);
    }
}
