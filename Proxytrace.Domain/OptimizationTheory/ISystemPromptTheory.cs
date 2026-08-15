using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Proposal;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Domain.OptimizationTheory;

/// <summary>
/// Theory that changing the agent's system prompt will improve it.
/// </summary>
public interface ISystemPromptTheory : IOptimizationTheory
{
    /// <summary>The full proposed system prompt text.</summary>
    string ProposedSystemMessage { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate ISystemPromptTheory CreateNew(
        IAgent agent,
        ITestSuite suite,
        TheorySource source,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        IReadOnlyCollection<Guid> evidenceTestRunIds);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate ISystemPromptTheory CreateExisting(
        IAgent agent,
        ITestSuite suite,
        TheoryStatus status,
        TheorySource source,
        Priority priority,
        string rationale,
        string proposedSystemMessage,
        IReadOnlyCollection<Guid> evidenceTestRunIds,
        Guid? resultingProposalId,
        double? baselinePassRate,
        double? projectedPassRate,
        double? pValue,
        Guid? abTestRunId,
        string contentHash,
        IDomainEntityData existing);
}
