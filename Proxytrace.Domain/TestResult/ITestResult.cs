using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestResult;

/// <summary>
/// Records the outcome of running a single <see cref="ITestCase"/>, including the actual response and evaluation verdict.
/// </summary>
public interface ITestResult : IDomainEntity<ITestResult>
{
    /// <summary>The test case that was executed.</summary>
    ITestCase TestCase { get; }

    /// <summary>The actual assistant response produced during the test run.</summary>
    AssistantMessage ActualResponse { get; }

    /// <summary>
    /// The overall score from combining all evaluations
    /// </summary>
    EvaluationScore? OverallScore { get; }

    /// <summary>
    /// Whether the test has passed
    /// </summary>
    bool Passed { get; }

    /// <summary>The evaluation verdict comparing the actual response against the expected output.</summary>
    IReadOnlyCollection<IEvaluation> Evaluations { get; }

    /// <summary>Wall-clock latency of the LLM call that produced this result.</summary>
    TimeSpan Latency { get; }

    /// <summary>Token usage of the LLM call that produced this result, if reported by the provider.</summary>
    TokenUsage? Usage { get; }

    /// <summary>Factory delegate for creating a new test result.</summary>
    public delegate ITestResult CreateNew(
        ITestCase testCase,
        ICompletion completion,
        IReadOnlyCollection<IEvaluation> evaluations);

    /// <summary>Factory delegate for reconstituting an existing test result from persistence.</summary>
    public delegate ITestResult CreateExisting(
        ITestCase testCase,
        AssistantMessage actualResponse,
        IReadOnlyCollection<IEvaluation> evaluations,
        TimeSpan latency,
        TokenUsage? usage,
        IDomainEntityData existing);

    /// <summary>
    /// Adds the evaluation to the test result
    /// </summary>
    Task<ITestResult> AddEvaluationAsync(
        IEvaluation evaluation,
        CancellationToken cancellationToken = default);
}
