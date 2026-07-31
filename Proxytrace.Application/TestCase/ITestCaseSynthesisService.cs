using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.TestSuite;

namespace Proxytrace.Application.TestCase;

/// <summary>
/// Proposes the test cases worth building from a captured conversation. Read-only: it writes
/// nothing and persists nothing — the proposals are reviewed and approved by a human, who then
/// creates the cases through the ordinary test-suite endpoints.
/// </summary>
public interface ITestCaseSynthesisService
{
    /// <summary>
    /// Analyses the whole conversation <paramref name="origin"/> belongs to and proposes cases.
    /// </summary>
    /// <param name="origin">The trace the user started from; defines the conversation and the agent.</param>
    /// <param name="destination">
    /// The suite the cases would land in, when one is chosen — its evaluators tell the agent whether
    /// a proposal can actually be scored. Null when no destination has been picked yet.
    /// </param>
    /// <param name="priorRounds">Completed exchanges, oldest first; the agent revises rather than restarts.</param>
    /// <param name="instruction">The user's special request for this round, if any.</param>
    Task<TestCaseProposalSet> SynthesizeAsync(
        IAgentCall origin,
        ITestSuite? destination,
        IReadOnlyList<SynthesisRound> priorRounds,
        string? instruction,
        CancellationToken cancellationToken = default);
}
