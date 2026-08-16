using Proxytrace.Api.Dto.Proposals;
using Proxytrace.Api.Dto.Tools;
using Proxytrace.Domain.OptimizationTheory;

namespace Proxytrace.Api.Dto.Theories;

/// <summary>
/// Maps <see cref="IOptimizationTheory"/> domain entities to <see cref="TheoryDto"/>.
/// </summary>
public sealed class TheoryDtoMapper
{
    private readonly ToolDtoMapper toolDtoMapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="TheoryDtoMapper"/> class.
    /// </summary>
    public TheoryDtoMapper(ToolDtoMapper toolDtoMapper)
    {
        this.toolDtoMapper = toolDtoMapper;
    }

    /// <summary>
    /// To dto.
    /// </summary>
    public TheoryDto ToDto(IOptimizationTheory t)
        => new(
            t.Id,
            t.Kind,
            t.Status,
            t.Source,
            t.Agent.Id,
            t.Agent.Name,
            t.Suite.Id,
            t.Priority,
            t.Rationale,
            ToDetailsDto(t),
            [.. t.EvidenceTestRunIds],
            t.ResultingProposalId,
            t.BaselinePassRate,
            t.ProjectedPassRate,
            t.PValue,
            t.ABTestRunId,
            t.CreatedAt,
            t.UpdatedAt);

    private ProposalDetailsDto ToDetailsDto(IOptimizationTheory t)
        => t switch
        {
            IModelSwitchTheory ms => new ModelSwitchDetailsDto(
                ms.ProposedEndpoint.Id,
                ms.Agent.Endpoint.Model.Name,
                ms.ProposedEndpoint.Model.Name,
                ExpectedCostDelta: null,
                ExpectedLatencyMs: null),
            ISystemPromptTheory sp => new SystemPromptDetailsDto(
                sp.Agent.SystemPrompt.Template,
                sp.ProposedSystemMessage),
            IToolUpdateTheory tu => new ToolDetailsDto(
                [.. tu.Agent.Tools.Select(toolDtoMapper.ToToolSpecDto)],
                [.. tu.ProposedTools.Select(toolDtoMapper.ToToolSpecDto)]),
            _ => throw new ArgumentOutOfRangeException(nameof(t)),
        };
}
