using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentVersion;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Domain.OptimizationProposal.Internal;

/// <summary>
/// Shared base for the concrete proposal kinds. Holds the common review metadata and the
/// Draft → Accepted/Rejected → Adopted lifecycle state machine; each subtype contributes the
/// proposed-change payload (and its validation). Mirrors <see cref="OptimizationTheory.Internal.OptimizationTheory"/>.
/// </summary>
internal abstract record OptimizationProposal : DomainEntity<IOptimizationProposal>, IOptimizationProposal
{
    public IAgent Agent { get; private init; }
    public abstract ProposalKind Kind { get; }
    public ProposalStatus Status { get; private init; }
    public Priority Priority { get; private init; }
    public string Rationale { get; private init; }
    public ITestRun ABTestRun { get; private init; }
    public double? CurrentPassRate { get; private init; }
    public double? ProposedPassRate { get; private init; }
    public IReadOnlyCollection<Guid> EvidenceTestRunIds { get; private init; }
    public string ContentHash { get; private init; }
    public DateTimeOffset? AdoptedAt { get; private init; }
    public Guid? AdoptedAgentVersionId { get; private init; }
    public int? AdoptedAgentVersionNumber { get; private init; }
    public bool? AdoptedManually { get; private init; }

    protected OptimizationProposal(
        IAgent agent,
        Priority priority,
        string rationale,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        string contentHash,
        IRepository<IOptimizationProposal> repository) : base(repository)
    {
        Agent = agent;
        Status = ProposalStatus.Draft;
        Priority = priority;
        Rationale = rationale;
        CurrentPassRate = currentPassRate;
        ProposedPassRate = proposedPassRate;
        EvidenceTestRunIds = evidenceTestRunIds.ToArray();
        ABTestRun = abTestRun;
        ContentHash = contentHash;
    }

    protected OptimizationProposal(
        IAgent agent,
        ProposalStatus status,
        Priority priority,
        string rationale,
        double? currentPassRate,
        double? proposedPassRate,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        ITestRun abTestRun,
        string contentHash,
        DateTimeOffset? adoptedAt,
        Guid? adoptedAgentVersionId,
        int? adoptedAgentVersionNumber,
        bool? adoptedManually,
        IDomainEntityData existing,
        IRepository<IOptimizationProposal> repository) : base(existing, repository)
    {
        Agent = agent;
        Status = status;
        Priority = priority;
        Rationale = rationale;
        CurrentPassRate = currentPassRate;
        ProposedPassRate = proposedPassRate;
        EvidenceTestRunIds = evidenceTestRunIds.ToArray();
        ABTestRun = abTestRun;
        ContentHash = contentHash;
        AdoptedAt = adoptedAt;
        AdoptedAgentVersionId = adoptedAgentVersionId;
        AdoptedAgentVersionNumber = adoptedAgentVersionNumber;
        AdoptedManually = adoptedManually;
    }

    public Task<IOptimizationProposal> Accept(CancellationToken cancellationToken = default)
    {
        if (Status != ProposalStatus.Draft)
            throw new InvalidOperationException($"Cannot accept proposal {Id} from status {Status}.");

        return ApplyAsync(this with { Status = ProposalStatus.Accepted }, cancellationToken);
    }

    public Task<IOptimizationProposal> Reject(CancellationToken cancellationToken = default)
    {
        if (Status != ProposalStatus.Draft)
            throw new InvalidOperationException($"Cannot reject proposal {Id} from status {Status}.");

        return ApplyAsync(this with { Status = ProposalStatus.Rejected }, cancellationToken);
    }

    public Task<IOptimizationProposal> MarkAdopted(
        IAgentVersion? adoptedVersion,
        bool manual,
        CancellationToken cancellationToken = default)
    {
        if (Status != ProposalStatus.Accepted)
            throw new InvalidOperationException($"Cannot mark proposal {Id} adopted from status {Status}.");

        return ApplyAsync(
            this with
            {
                Status = ProposalStatus.Adopted,
                AdoptedAt = DateTimeOffset.UtcNow,
                AdoptedAgentVersionId = adoptedVersion?.Id,
                AdoptedAgentVersionNumber = adoptedVersion?.VersionNumber,
                AdoptedManually = manual,
            },
            cancellationToken);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Agent.Validate(validationContext))
            yield return result;

        foreach (var result in ABTestRun.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(Rationale);
        yield return Validation.NotNullOrWhiteSpace(ContentHash);

        if (CurrentPassRate is { } currentPassRate &&
            (!double.IsFinite(currentPassRate) || currentPassRate is < 0 or > 1))
            yield return new ValidationResult(
                $"{nameof(CurrentPassRate)} must be a finite value between 0 and 1.",
                [nameof(CurrentPassRate)]);

        if (ProposedPassRate is { } proposedPassRate &&
            (!double.IsFinite(proposedPassRate) || proposedPassRate is < 0 or > 1))
            yield return new ValidationResult(
                $"{nameof(ProposedPassRate)} must be a finite value between 0 and 1.",
                [nameof(ProposedPassRate)]);

        if (Status == ProposalStatus.Adopted)
        {
            yield return Validation.NotNull(AdoptedAt);
            yield return Validation.NotNull(AdoptedManually);

            if (AdoptedAt is { } adoptedAt)
            {
                yield return Validation.InPast(adoptedAt, nameof(AdoptedAt));
                yield return Validation.NotBefore(adoptedAt, CreatedAt, nameof(AdoptedAt));
            }
        }
        else
        {
            yield return Validation.Null(AdoptedAt);
            yield return Validation.Null(AdoptedAgentVersionId);
            yield return Validation.Null(AdoptedAgentVersionNumber);
            yield return Validation.Null(AdoptedManually);
        }
    }
}
