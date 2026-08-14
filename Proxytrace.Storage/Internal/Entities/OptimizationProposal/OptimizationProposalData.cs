using Nordstein.Core.AI.Tools;

namespace Proxytrace.Storage.Internal.Entities.OptimizationProposal;

internal sealed record ModelSwitchProposalData(
    Guid ProposedEndpointId,
    decimal? ExpectedCostDelta,
    TimeSpan? ExpectedLatencyDelta);

internal sealed record SystemPromptProposalData(string ProposedSystemMessage);

internal sealed record ToolUpdateProposalData(IReadOnlyList<ToolSpecification> ProposedTools);
