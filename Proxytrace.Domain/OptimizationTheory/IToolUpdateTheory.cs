using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.AI.Tools;

namespace Proxytrace.Domain.OptimizationTheory;

/// <summary>
/// Theory that updating the agent's tool definitions will improve it.
/// </summary>
public interface IToolUpdateTheory : IOptimizationTheory
{
    /// <summary>The proposed tool specifications, replacing the agent's current ones.</summary>
    IReadOnlyList<ToolSpecification> ProposedTools { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IToolUpdateTheory CreateNew(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        IReadOnlyCollection<Guid> evidenceTestRunIds);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IToolUpdateTheory CreateExisting(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        IReadOnlyList<ToolSpecification> proposedTools,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing);
}
