using Proxytrace.Domain.Evaluation;

namespace Proxytrace.Domain.TestResult;

/// <summary>
/// Canonical pass/fail definition for a test result, shared by the optimization-theory
/// validators, the stored proposal pass-rates, and the A/B summary shown in the UI — so all
/// three agree. A result passes only when at least one evaluation actually produced a verdict
/// and every such verdict passed (an acceptable score). The "at least one" guard matters:
/// <c>All()</c> over an empty set is vacuously true, which would otherwise count an
/// unevaluated result as a pass.
///
/// An <b>errored</b> evaluation is excluded from the verdict rather than counted as a failing
/// one: a judge that crashed or answered unparseably says nothing about the agent, and treating
/// it as a fail makes a broken evaluator indistinguishable from a defect — which drags a run's
/// pass rate down and biases the A/B test that reads it. A result whose evaluations *all*
/// errored has no verdict at all and therefore still does not pass.
/// </summary>
public static class TestResultExtensions
{
    /// <summary>
    /// Determines whether the pass.
    /// </summary>
    public static bool IsPass(this ITestResult result)
    {
        var judged = result.Evaluations.Where(e => !e.IsErrored()).ToList();
        return judged.Count > 0 && judged.All(e => e.Passed);
    }
}
