using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;

namespace Proxytrace.Api.Dto.Proposals;

/// <summary>
/// Data transfer object representing a optimization proposal.
/// </summary>
public record OptimizationProposalDto(
    Guid Id,
    ProposalKind Kind,
    ProposalStatus Status,
    Guid AgentId,
    string AgentName,
    Priority Priority,
    string Rationale,
    ProposalDetailsDto Details,
    Guid[] EvidenceTestRunIds,
    AbTestRunSummaryDto? AbTestRun,
    double? CurrentPassRate,
    double? ProposedPassRate,
    double? ExpectedPassRateDelta,
    DateTimeOffset? AdoptedAt,
    Guid? AdoptedAgentVersionId,
    int? AdoptedAgentVersionNumber,
    bool? AdoptedManually,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>
/// Request payload for update proposal status operations.
/// </summary>
public record UpdateProposalStatusRequest(ProposalStatus Status);
