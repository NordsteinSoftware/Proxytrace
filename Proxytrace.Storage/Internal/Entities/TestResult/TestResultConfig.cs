using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Serialization;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluation.Internal;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.Usage;
using Proxytrace.Storage.Internal.Entities.TestCase;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

internal class TestResultConfig : AbstractEntityConfiguration<TestResultEntity>, IMapper<ITestResult, TestResultEntity>
{
    private readonly IRepository<ITestCase> testCases;
    private readonly IRepository<IEvaluator> evaluators;
    private readonly ITestResult.CreateExisting factory;
    private readonly IEvaluation.Create createEvaluation;
    private readonly IEvaluation.CreateErrored createErroredEvaluation;
    private readonly ISerializer serializer;

    public TestResultConfig(
        IRepository<ITestCase> testCases,
        IRepository<IEvaluator> evaluators,
        ITestResult.CreateExisting factory,
        IEvaluation.Create createEvaluation,
        IEvaluation.CreateErrored createErroredEvaluation,
        ISerializer serializer)
    {
        this.testCases = testCases;
        this.evaluators = evaluators;
        this.factory = factory;
        this.createEvaluation = createEvaluation;
        this.createErroredEvaluation = createErroredEvaluation;
        this.serializer = serializer;
    }

    public override void Configure(EntityTypeBuilder<TestResultEntity> builder)
    {
        // The evaluator-history queries scan the most recent N results ordered by CreatedAt;
        // without this index every scan sorts the whole table.
        builder.HasIndex(e => e.CreatedAt);

        builder
            .HasOne<TestCaseEntity>()
            .WithMany()
            .HasForeignKey(e => e.TestCase)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .Property(e => e.ActualResponse)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.DeserializeRequired<AssistantMessage>(v)
            );

        builder
            .Property(e => e.Evaluations)
            .HasConversion(
                v => serializer.Serialize(v),
                v => serializer.Deserialize<IReadOnlyCollection<StoredEvaluation>>(v) ?? Array.Empty<StoredEvaluation>()
            );
    }

    public async Task<ITestResult> Map(TestResultEntity stored, CancellationToken cancellationToken = default)
    {
        var evaluations = new List<IEvaluation>();
        foreach (var e in stored.Evaluations)
        {
            var evaluator = await evaluators.GetAsync(e.EvaluatorId, cancellationToken);
            TimeSpan evalLatency = TimeSpan.FromMicroseconds(e.LatencyMicroseconds);
            TokenUsage? evalUsage = e.InputTokens.HasValue && e.OutputTokens.HasValue
                ? new TokenUsage((ulong)e.InputTokens.Value, (ulong)e.OutputTokens.Value, (ulong)(e.CachedInputTokens ?? 0))
                : null;

            if (!string.IsNullOrWhiteSpace(e.ErrorMessage))
            {
                evaluations.Add(createErroredEvaluation(evaluator, evalLatency, new StoredEvaluationException(e.ErrorMessage)));
            }
            else if (e.Score.HasValue)
            {
                evaluations.Add(createEvaluation(evaluator, e.Score.Value, evalLatency, evalUsage, e.Cost, e.Reasoning));
            }
        }

        TokenUsage? usage = stored.InputTokens.HasValue && stored.OutputTokens.HasValue
            ? new TokenUsage((ulong)stored.InputTokens.Value, (ulong)stored.OutputTokens.Value, (ulong)(stored.CachedInputTokens ?? 0))
            : null;

        return factory(
            testCase: await testCases.GetAsync(stored.TestCase, cancellationToken),
            actualResponse: stored.ActualResponse,
            evaluations: evaluations,
            latency: TimeSpan.FromMicroseconds(stored.DurationMs),
            usage: usage,
            existing: stored);
    }

    public Task<TestResultEntity> Map(ITestResult domain, CancellationToken cancellationToken = default)
        => new TestResultEntity
        {
            Id = domain.Id,
            TestCase = domain.TestCase.Id,
            ActualResponse = domain.ActualResponse,
            Evaluations = domain.Evaluations
                .Select(e => new StoredEvaluation
                {
                    EvaluatorId = e.Evaluator.Id,
                    Score = e.Score,
                    Reasoning = e.Reasoning,
                    ErrorMessage = e.ErrorMessage,
                    InputTokens = (long?)e.TokenUsage?.InputTokenCount,
                    OutputTokens = (long?)e.TokenUsage?.OutputTokenCount,
                    CachedInputTokens = (long?)e.TokenUsage?.CachedInputTokenCount,
                    LatencyMicroseconds = (long)e.Latency.TotalMicroseconds,
                    Cost = e.Cost,
                })
                .ToArray(),
            DurationMs = (long)domain.Latency.TotalMicroseconds,
            InputTokens = (long?)domain.Usage?.InputTokenCount,
            OutputTokens = (long?)domain.Usage?.OutputTokenCount,
            CachedInputTokens = (long?)domain.Usage?.CachedInputTokenCount,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt,
            // Queryable projection of the evaluations above; CreatedAt copied from the parent so the
            // evaluator-stats queries range/bucket on the child row without joining back. See
            // EvaluationStatEntity. Written on insert; on update the repository rebuilds the rows
            // from this mapping (TestResultRepository.UpdateRelationsAsync) so they follow a
            // rewritten CreatedAt (the demo seed's statistics backdating).
            EvaluationStats = domain.Evaluations
                .Select(e => new EvaluationStatEntity
                {
                    Id = Guid.NewGuid(),
                    TestResultId = domain.Id,
                    EvaluatorId = e.Evaluator.Id,
                    CreatedAt = domain.CreatedAt,
                    Score = e.Score,
                    HasError = e.ErrorMessage is not null,
                    InputTokens = (long?)e.TokenUsage?.InputTokenCount,
                    OutputTokens = (long?)e.TokenUsage?.OutputTokenCount,
                    CachedInputTokens = (long?)e.TokenUsage?.CachedInputTokenCount,
                    LatencyMicroseconds = (long)e.Latency.TotalMicroseconds,
                    Cost = e.Cost,
                })
                .ToList(),
        }.ToTaskResult();
}
