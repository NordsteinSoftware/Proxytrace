using System.ComponentModel.DataAnnotations;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.TestCase;

namespace Proxytrace.Domain.TestResult.Internal;

internal record TestResult : DomainEntity<ITestResult>, ITestResult
{
    /// <summary>
    /// Gets or sets the test case.
    /// </summary>
    public ITestCase TestCase { get; init; }
    /// <summary>
    /// Gets or sets the actual response.
    /// </summary>
    public AssistantMessage ActualResponse { get; init; }
    /// <summary>
    /// Gets the passed.
    /// </summary>
    public bool Passed => this.IsPass();
    /// <summary>
    /// Gets or sets the evaluations.
    /// </summary>
    public IReadOnlyCollection<IEvaluation> Evaluations { get; init; }
    /// <summary>
    /// Gets or sets the latency.
    /// </summary>
    public TimeSpan Latency { get; init; }
    /// <summary>
    /// Gets or sets the usage.
    /// </summary>
    public TokenUsage? Usage { get; init; }
    /// <summary>
    /// Gets the overall score.
    /// </summary>
    public EvaluationScore? OverallScore => Evaluations.CombineScores();

    /// <summary>
    /// Initializes a new instance of the <see cref="TestResult"/> class.
    /// </summary>
    public TestResult(
        ITestCase testCase,
        ICompletion completion,
        IReadOnlyCollection<IEvaluation> evaluations,
        IRepository<ITestResult> repository) : base(repository)
    {
        TestCase = testCase;
        ActualResponse = completion.Response;
        Evaluations = evaluations;
        Latency = completion.Latency;
        Usage = completion.Usage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TestResult"/> class.
    /// </summary>
    public TestResult(
        ITestCase testCase,
        AssistantMessage actualResponse,
        IReadOnlyCollection<IEvaluation> evaluations,
        TimeSpan latency,
        TokenUsage? usage,
        IDomainEntityData existing,
        IRepository<ITestResult> repository) : base(existing, repository)
    {
        TestCase = testCase;
        ActualResponse = actualResponse;
        Evaluations = evaluations;
        Latency = latency;
        Usage = usage;
    }

    /// <summary>
    /// Adds the evaluation asynchronously.
    /// </summary>
    public Task<ITestResult> AddEvaluationAsync(IEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IEvaluation> updatedEvaluations =
        [
            ..Evaluations.Where(x => x.Evaluator.Id != evaluation.Evaluator.Id),
            evaluation
        ];

        return ApplyAsync(this with { Evaluations = updatedEvaluations }, cancellationToken);
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in TestCase.Validate(validationContext))
        {
            yield return result;
        }

        foreach (var result in ActualResponse.Validate(validationContext))
        {
            yield return result;
        }

        foreach (IEvaluation evaluation in Evaluations)
        {
            foreach (var result in evaluation.Validate(validationContext))
            {
                yield return result;
            }
        }
    }
}
