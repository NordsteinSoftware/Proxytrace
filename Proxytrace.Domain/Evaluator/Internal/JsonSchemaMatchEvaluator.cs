using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using JetBrains.Annotations;
using Json.Schema;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Domain.Evaluator.Internal;

[UsedImplicitly]
internal record JsonSchemaMatchEvaluator : DomainEntity<IEvaluator>, IJsonSchemaMatchEvaluator
{
    private readonly IEvaluation.Create evaluationFactory;

    public string Name
        => "Json Schema Match";

    public EvaluatorKind Kind
        => EvaluatorKind.JsonSchemaMatch;

    public IProject Project { get; }

    public string JsonSchema { get; }

    public JsonSchemaMatchEvaluator(
        string jsonSchema,
        IProject project,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(repository)
    {
        JsonSchema = jsonSchema;
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    public JsonSchemaMatchEvaluator(
        string jsonSchema,
        IProject project,
        IDomainEntityData existing,
        IEvaluation.Create evaluationFactory,
        IRepository<IEvaluator> repository) : base(existing, repository)
    {
        JsonSchema = jsonSchema;
        Project = project;
        this.evaluationFactory = evaluationFactory;
    }

    public Task<IEvaluation?> EvaluateAsync(
        ITestResult testResult,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        var actualText = testResult.ActualResponse.GetTextResponse();

        EvaluationScore score;
        string? reasoning = null;

        try
        {
            var schema = Json.Schema.JsonSchema.FromText(JsonSchema);
            using var document = JsonDocument.Parse(actualText);
            var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

            if (result.IsValid)
            {
                score = EvaluationScore.Excellent;
            }
            else
            {
                score = EvaluationScore.Terrible;
                var errors = (result.Details ?? [])
                    .Where(d => d.Errors is { Count: > 0 })
                    .SelectMany(d => (d.Errors ?? new Dictionary<string, string>())
                        .Select(e => $"{d.InstanceLocation}: {e.Key} — {e.Value}"));
                reasoning = string.Join(Environment.NewLine, errors);
            }
        }
        catch (JsonException ex)
        {
            score = EvaluationScore.Terrible;
            reasoning = $"Response is not valid JSON: {ex.Message}";
        }

        return Task.FromResult<IEvaluation?>(evaluationFactory(this, score, sw.Elapsed, reasoning: reasoning));
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        foreach (var result in Project.Validate(validationContext))
            yield return result;

        yield return Validation.NotNullOrWhiteSpace(JsonSchema);
        yield return Validation.Json(JsonSchema);
    }
}
