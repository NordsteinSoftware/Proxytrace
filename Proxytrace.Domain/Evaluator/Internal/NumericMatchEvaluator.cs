using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Domain.Evaluator.Internal;

[UsedImplicitly]
internal record NumericMatchEvaluator : DomainEntity<IEvaluator>, INumericMatchEvaluator
{
    private readonly IEvaluation.Create evaluationFactory;

    /// <summary>
    /// Provides additional functionality.
    /// </summary>
    public string Name
        => "Numeric Match";

    /// <summary>
    /// Provides additional functionality.
    /// </summary>
    public EvaluatorKind Kind
        => EvaluatorKind.NumericMatch;

    /// <summary>
    /// Gets the project.
    /// </summary>
    public IProject Project { get; }

    /// <summary>
    /// Gets the extraction pattern.
    /// </summary>
    public Regex ExtractionPattern { get; }
    /// <summary>
    /// Gets the tolerance.
    /// </summary>
    public decimal Tolerance { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NumericMatchEvaluator"/> class.
    /// </summary>
    public NumericMatchEvaluator(
        Regex extractionPattern,
        decimal tolerance,
        IProject project,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(repository)
    {
        ExtractionPattern = extractionPattern;
        Tolerance = tolerance;
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NumericMatchEvaluator"/> class.
    /// </summary>
    public NumericMatchEvaluator(
        Regex extractionPattern,
        decimal tolerance,
        IProject project,
        IDomainEntityData existing,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(existing, repository)
    {
        ExtractionPattern = extractionPattern;
        Tolerance = tolerance;
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    /// <summary>
    /// Evaluates asynchronously.
    /// </summary>
    public Task<IEvaluation?> EvaluateAsync(
        ITestResult testResult,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        var expectedText = testResult.TestCase.ExpectedOutput.GetTextResponse();
        var expectedMatch = ExtractionPattern.Match(expectedText);
        if (!expectedMatch.Success || !TryParseInvariant(expectedMatch.Value, out var expected))
            return Task.FromResult<IEvaluation?>(null);
        
        var actualText = testResult.ActualResponse.GetTextResponse();
        var actualMatch = ExtractionPattern.Match(actualText);

        EvaluationScore score;
        string? reasoning = null;

        if (!actualMatch.Success)
        {
            score = EvaluationScore.Terrible;
            reasoning = "Actual response did not match the extraction pattern.";
        }
        else if (!TryParseInvariant(actualMatch.Value, out var actual))
        {
            score = EvaluationScore.Terrible;
            reasoning = $"Could not parse actual value '{actualMatch.Value}' as a number.";
        }
        else
        {
            var delta = Math.Abs(actual - expected);
            if (delta <= Tolerance)
            {
                score = EvaluationScore.Excellent;
            }
            else
            {
                score = EvaluationScore.Terrible;
                reasoning = $"Expected {expected} ± {Tolerance} but got {actual} (delta: {delta}).";
            }
        }

        return Task.FromResult<IEvaluation?>(evaluationFactory(this, score, sw.Elapsed, reasoning: reasoning));
    }

    /// <summary>
    /// Parses a numeric value with invariant culture so scoring never depends on the server
    /// thread culture (e.g. a de-DE host must not read "3.14" as 314).
    /// </summary>
    private static bool TryParseInvariant(string value, out decimal result)
        => decimal.TryParse(
            value,
            NumberStyles.Number | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out result);

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Project.Validate(validationContext))
            yield return result;

        // ExtractionPattern is a non-nullable Regex (no NotNull needed). Tolerance of 0 is a valid
        // exact-match configuration (delta <= 0); only a negative tolerance is invalid.
        yield return Validation.NotNegative(Tolerance);
    }
}
