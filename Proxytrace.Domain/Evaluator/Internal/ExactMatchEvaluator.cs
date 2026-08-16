using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using JetBrains.Annotations;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Domain.Evaluator.Internal;

[UsedImplicitly]
internal record ExactMatchEvaluator : DomainEntity<IEvaluator>, IExactMatchEvaluator
{
    private readonly IEvaluation.Create evaluationFactory;

    /// <summary>
    /// Provides additional functionality.
    /// </summary>
    public string Name 
        => "Exact Match";

    /// <summary>
    /// Provides additional functionality.
    /// </summary>
    public EvaluatorKind Kind
        => EvaluatorKind.ExactMatch;

    /// <summary>
    /// Gets the project.
    /// </summary>
    public IProject Project { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactMatchEvaluator"/> class.
    /// </summary>
    public ExactMatchEvaluator(
        IProject project,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(repository)
    {
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactMatchEvaluator"/> class.
    /// </summary>
    public ExactMatchEvaluator(
        IProject project,
        IDomainEntityData existing,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(existing, repository)
    {
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    /// <summary>
    /// Evaluates the actual output against the expected output, given the input conversation.
    /// </summary>
    public Task<IEvaluation?> EvaluateAsync(
        ITestResult testResult,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        var expectedOutput = testResult.TestCase.ExpectedOutput;
        var actualOutput = testResult.ActualResponse;

        List<string> differences = [];

        if (expectedOutput.Contents.Count != actualOutput.Contents.Count)
        {
            // An exact match requires the same number of content parts. Zip alone would silently
            // truncate to the shorter sequence, passing a partial or padded response.
            differences.Add(
                $"Expected {expectedOutput.Contents.Count} content part(s) but got {actualOutput.Contents.Count}.");
        }
        else
        {
            differences.AddRange(expectedOutput.Contents
                .Zip(actualOutput.Contents, (expected, actual) => (Expected: expected, Actual: actual))
                .Where(pair => !pair.Expected.Equals(pair.Actual))
                .Select(pair => $"Expected '{pair.Expected}' but got '{pair.Actual}'"));
        }

        // Tool requests live beside Contents, not inside them, so a tool-call expectation is
        // invisible to a content-only comparison — an expected call contributes zero content parts
        // and matched any tool-free response. Compared id-blind and unordered; see ToolRequestMatch.
        differences.AddRange(
            ToolRequestMatch.Differences(expectedOutput.ToolRequests, actualOutput.ToolRequests));

        EvaluationScore score = differences.Count == 0 ? EvaluationScore.Acceptable : EvaluationScore.Terrible;
        string? reasoning = differences.Count == 0
            ? null
            : string.Join(Environment.NewLine, differences);

        IEvaluation evaluation = evaluationFactory(
            this,
            score,
            sw.Elapsed,
            reasoning: reasoning);
        return Task.FromResult<IEvaluation?>(evaluation);
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Project.Validate(validationContext))
            yield return result;
    }
}
