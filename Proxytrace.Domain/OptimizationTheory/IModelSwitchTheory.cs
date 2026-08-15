using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory;

/// <summary>
/// Theory that switching the agent's endpoint to a different model will improve it.
/// </summary>
public interface IModelSwitchTheory : IOptimizationTheory
{
    /// <summary>The endpoint proposed as a replacement for the agent's current one.</summary>
    IModelEndpoint ProposedEndpoint { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IModelSwitchTheory CreateNew(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        IReadOnlyCollection<Guid> evidenceTestRunIds);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IModelSwitchTheory CreateExisting(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        IModelEndpoint proposedEndpoint,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing);
}
