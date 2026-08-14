using Nordstein.Core.AI.Clients;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using JetBrains.Annotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;
using Nordstein.Core.AI.Completions;

namespace Proxytrace.Domain.Evaluator.Internal;

[UsedImplicitly]
internal sealed record AgenticEvaluator : DomainEntity<IEvaluator>, IAgenticEvaluator
{
    private readonly IEvaluation.Create evaluationFactory;
    private readonly IEvaluation.CreateErrored erroredFactory;

    public IAgent Agent { get; }

    public string Name => Agent.Name;

    public EvaluatorKind Kind
        => EvaluatorKind.Agentic;

    public IProject Project
        => Agent.Project;

    public AgenticEvaluator(
        IAgent agent,
        IEvaluation.Create evaluationFactory,
        IEvaluation.CreateErrored erroredFactory,
        IRepository<IEvaluator> repository) : base(repository)
    {
        Agent = agent;
        this.evaluationFactory = evaluationFactory;
        this.erroredFactory = erroredFactory;
    }

    public AgenticEvaluator(
        IAgent agent,
        IDomainEntityData existing,
        IEvaluation.Create evaluationFactory,
        IEvaluation.CreateErrored erroredFactory,
        IRepository<IEvaluator> repository) : base(existing, repository)
    {
        Agent = agent;
        this.evaluationFactory = evaluationFactory;
        this.erroredFactory = erroredFactory;
    }

    public async Task<IEvaluation?> EvaluateAsync(ITestResult testResult, CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            Conversation conversation = Conversation.Create().With(BuildEvaluationMessage(testResult));

            TypedCompletion<AgenticEvaluatorResult> completion;
            try
            {
                completion = await JudgeAsync(conversation, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sampling is non-deterministic, so the usual cause of a failure here — a verdict cut
                // off mid-sentence because the judge talked past its output budget — does not repeat.
                // One retry costs a judge call; not retrying persists an errored evaluation, which
                // the pass-rate math then has to treat as a case that was never judged.
                completion = await JudgeAsync(conversation, cancellationToken);
            }

            if (completion.Response is null)
            {
                throw new InvalidOperationException("Agent response was null");
            }
            
            TokenUsage? usage = completion.Usage;
            decimal? cost = usage != null ? Agent.Endpoint.CalculateCost(usage) : null;

            return evaluationFactory(
                this, 
                completion.Response.Score, 
                completion.Latency, 
                usage, 
                cost, 
                completion.Response.Reasoning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Let cancellation propagate rather than persisting it as a normal "errored" evaluation.
            return erroredFactory(this, sw.Elapsed, ex);
        }
    }

    private async Task<TypedCompletion<AgenticEvaluatorResult>> JudgeAsync(
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        using var client = Agent.CreateClient();
        return await client.CompleteAsync<AgenticEvaluatorResult>(
            conversation,
            cancellationToken: cancellationToken);
    }

    private UserMessage BuildEvaluationMessage(ITestResult testResult)
    {
        string content = $"""
                          # INPUT
                          "{testResult.TestCase.Input}"

                          # EXPECTED OUTPUT
                          "{testResult.TestCase.ExpectedOutput}"

                          # ACTUAL OUTPUT
                          "{testResult.ActualResponse}"
                          """;

        return Message.CreateUserMessage(content);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var validationResult in base.Validate(validationContext))
        {
            yield return validationResult;
        }

        foreach (var validationResult in Agent.Validate(validationContext))
        {
            yield return validationResult;
        }

        yield return Validation.True(Agent.IsSystemAgent);
    }

    /// <summary>
    /// The judge's verdict. <see cref="Reasoning"/> is the only unbounded field, and it is what runs
    /// a response out of output budget — the answer is then cut mid-string, the JSON no longer
    /// parses, and a perfectly good <see cref="Score"/> is thrown away with it. The description is
    /// exported into the JSON schema the judge is prompted with, so the budget reaches the model.
    /// </summary>
    [UsedImplicitly]
    private record AgenticEvaluatorResult(
        EvaluationScore Score,
        [property: Description(
            "A brief justification for the score: at most 3 sentences and 600 characters. " +
            "Be concise — a longer answer risks being cut off before it is complete.")]
        string? Reasoning);
}
