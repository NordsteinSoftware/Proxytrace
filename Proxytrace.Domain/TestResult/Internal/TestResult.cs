using System.ComponentModel.DataAnnotations;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.Usage;

namespace Proxytrace.Domain.TestResult.Internal;

internal record TestResult : DomainEntity<ITestResult>, ITestResult
{
    public ITestCase TestCase { get; init; }
    public AssistantMessage ActualResponse { get; init; }
    public bool Passed => this.IsPass();
    public IReadOnlyCollection<IEvaluation> Evaluations { get; init; }
    public TimeSpan Latency { get; init; }
    public TokenUsage? Usage { get; init; }
    public EvaluationScore? OverallScore => Evaluations.CombineScores();

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

    public Task<ITestResult> AddEvaluationAsync(IEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IEvaluation> updatedEvaluations =
        [
            ..Evaluations.Where(x => x.Evaluator.Id != evaluation.Evaluator.Id),
            evaluation
        ];

        return ApplyAsync(this with { Evaluations = updatedEvaluations }, cancellationToken);
    }

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
