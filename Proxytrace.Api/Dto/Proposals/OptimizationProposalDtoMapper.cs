using Proxytrace.Api.Dto.Tools;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;

namespace Proxytrace.Api.Dto.Proposals;

/// <summary>
/// Maps <see cref="IOptimizationProposal"/> domain entities to <see cref="OptimizationProposalDto"/>.
/// </summary>
public sealed class OptimizationProposalDtoMapper
{
    private readonly ToolDtoMapper toolDtoMapper;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptimizationProposalDtoMapper"/> class.
    /// </summary>
    public OptimizationProposalDtoMapper(ToolDtoMapper toolDtoMapper)
    {
        this.toolDtoMapper = toolDtoMapper;
    }

    /// <summary>
    /// To dto.
    /// </summary>
    public OptimizationProposalDto ToDto(IOptimizationProposal p)
        => new(
            p.Id,
            p.Kind,
            p.Status,
            p.Agent.Id,
            p.Agent.Name,
            p.Priority,
            p.Rationale,
            ToDetailsDto(p),
            [.. p.EvidenceTestRunIds],
            p.ABTestRun is not null ? ToAbTestRunSummaryDto(p.ABTestRun) : null,
            p.CurrentPassRate,
            p.ProposedPassRate,
            p.ExpectedPassRateDelta,
            p.AdoptedAt,
            p.AdoptedAgentVersionId,
            p.AdoptedAgentVersionNumber,
            p.AdoptedManually,
            p.CreatedAt,
            p.UpdatedAt);

    private const int ArtifactSchemaVersion = 1;

    /// <summary>
    /// To artifact dto.
    /// </summary>
    public ProposalArtifactDto ToArtifactDto(IOptimizationProposal p)
        => new(
            ArtifactSchemaVersion,
            p.Id,
            p.Kind,
            p.Status,
            DateTimeOffset.UtcNow,
            new ProposalArtifactAgentDto(p.Agent.Id, p.Agent.Name),
            p.Priority,
            p.Rationale,
            ToDetailsDto(p),
            new ProposalArtifactEvidenceDto(
                p.CurrentPassRate,
                p.ProposedPassRate,
                p.ExpectedPassRateDelta,
                [.. p.EvidenceTestRunIds],
                p.ABTestRun is not null ? ToAbTestRunSummaryDto(p.ABTestRun) : null),
            new ProposalArtifactAdoptionDto(
                p.AdoptedAt,
                p.AdoptedAgentVersionId,
                p.AdoptedAgentVersionNumber,
                p.AdoptedManually));

    private static AbTestRunSummaryDto ToAbTestRunSummaryDto(ITestRun r)
    {
        var passed = r.TestResults.Count(x => x.IsPass());
        var completed = r.TestResults.Count;
        var total = r.Group.Suite.TestCases.Count;
        var passRate = completed > 0 ? Math.Round((double)passed / completed * 100) : 0;
        // Aggregated per-case inference latency, not a wall-clock run timer — see RunLatency.
        long? durationMs = TestRuns.RunLatency.AverageInferenceMs(r);

        return new AbTestRunSummaryDto(
            Id: r.Id,
            GroupId: r.Group.Id,
            Status: r.Status,
            TotalCases: total,
            CompletedCases: completed,
            PassedCases: passed,
            FailedCases: completed - passed,
            PassRate: passRate,
            StartedAt: r.CreatedAt,
            CompletedAt: r.CompletedAt,
            DurationMs: durationMs);
    }

    private ProposalDetailsDto ToDetailsDto(IOptimizationProposal p)
        => p switch
        {
            IModelSwitchProposal ms => ToModelSwitchDto(ms),
            ISystemPromptProposal sp => ToSystemPromptDto(sp),
            IToolUpdateProposal tu => ToToolDto(tu),
            _ => throw new ArgumentOutOfRangeException(nameof(p)),
        };

    private static ModelSwitchDetailsDto ToModelSwitchDto(IModelSwitchProposal ms)
        => new(
            ms.ProposedEndpoint.Id,
            ms.Agent.Endpoint.Model.Name,
            ms.ProposedEndpoint.Model.Name,
            ms.ExpectedCostDelta.HasValue ? (double)ms.ExpectedCostDelta.Value : null,
            ms.ExpectedLatencyDelta.HasValue ? (long)ms.ExpectedLatencyDelta.Value.TotalMilliseconds : null);

    private static SystemPromptDetailsDto ToSystemPromptDto(ISystemPromptProposal sp)
        => new(sp.Agent.SystemPrompt.Template, sp.ProposedSystemMessage);

    private ToolDetailsDto ToToolDto(IToolUpdateProposal tu)
        => new(
            [.. tu.Agent.Tools.Select(toolDtoMapper.ToToolSpecDto)],
            [.. tu.ProposedTools.Select(toolDtoMapper.ToToolSpecDto)]);
}
