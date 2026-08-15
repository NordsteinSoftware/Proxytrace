using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;

namespace Proxytrace.Api.Dto.Proposals;

/// <summary>
/// Machine-readable handoff package for a promoted proposal: everything a developer (or a
/// CI job) needs to apply the proposed change to the agent's actual implementation.
/// Proxytrace is an observing proxy — it cannot change the client's agent code, so this
/// artifact is the contract between a promoted proposal and the team applying it.
/// </summary>
public record ProposalArtifactDto(
    int SchemaVersion,
    Guid ProposalId,
    ProposalKind Kind,
    ProposalStatus Status,
    DateTimeOffset GeneratedAt,
    ProposalArtifactAgentDto Agent,
    Priority Priority,
    string Rationale,
    ProposalDetailsDto Change,
    ProposalArtifactEvidenceDto Evidence,
    ProposalArtifactAdoptionDto Adoption);

/// <summary>
/// Data transfer object representing a proposal artifact agent.
/// </summary>
public record ProposalArtifactAgentDto(Guid Id, string Name);

/// <summary>
/// Data transfer object representing a proposal artifact evidence.
/// </summary>
public record ProposalArtifactEvidenceDto(
    double? CurrentPassRate,
    double? ProposedPassRate,
    double? ExpectedPassRateDelta,
    Guid[] EvidenceTestRunIds,
    AbTestRunSummaryDto? AbTestRun);

/// <summary>
/// Data transfer object representing a proposal artifact adoption.
/// </summary>
public record ProposalArtifactAdoptionDto(
    DateTimeOffset? AdoptedAt,
    Guid? AdoptedAgentVersionId,
    int? AdoptedAgentVersionNumber,
    bool? AdoptedManually);
