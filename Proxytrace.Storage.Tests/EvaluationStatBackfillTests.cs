using Autofac.Features.OwnedInstances;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Domain;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities.TestResult;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class EvaluationStatBackfillTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Backfill_RestoresProjectionForResultMissingIt()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);

        var result = await PersistResult(services, evaluator, EvaluationScore.Good, new TokenUsage(11, 22, 3), cost: 0.5m);

        // Simulate a test result written before the EvaluationStatEntity projection existed: the
        // authoritative evaluation stays in the JSON column, but the queryable projection row is gone.
        await ClearStatRows(services, result.Id);
        (await CountStatRows(services, result.Id)).Should().Be(0);

        // Bug reproduction: the evaluator-stats reader has nothing to aggregate, so the page shows zeroes.
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();
        var (from, to) = NowWindow();
        (await reader.GetOverviewAsync(evaluator.Id, from, to, StatisticsBucket.Daily, CancellationToken))
            .Summary.TotalEvaluations.Should().Be(0);

        var backfill = services.GetRequiredService<EvaluationStatBackfillService>();
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(1);

        var row = (await StatRows(services, result.Id)).Should().ContainSingle().Subject;
        row.EvaluatorId.Should().Be(evaluator.Id);
        row.CreatedAt.Should().Be(result.CreatedAt);
        row.Score.Should().Be(EvaluationScore.Good);
        row.HasError.Should().BeFalse();
        row.InputTokens.Should().Be(11);
        row.OutputTokens.Should().Be(22);
        row.CachedInputTokens.Should().Be(3);
        row.Cost.Should().Be(0.5m);

        // End to end: the reader now aggregates the restored evaluation, so the page shows real numbers.
        (await reader.GetOverviewAsync(evaluator.Id, from, to, StatisticsBucket.Daily, CancellationToken))
            .Summary.TotalEvaluations.Should().Be(1);
    }

    [TestMethod]
    public async Task Backfill_WhenEveryResultHasProjection_IsIdempotentNoOp()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);

        // The write path already populated the projection, so there is nothing to do — and a re-run
        // must not duplicate the existing rows.
        await PersistResult(services, evaluator, EvaluationScore.Good);

        var backfill = services.GetRequiredService<EvaluationStatBackfillService>();
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task Backfill_ProcessesEveryResultAcrossMultipleBatches()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);
        var ownedContextFactory = services.GetRequiredService<Func<Owned<StorageDbContext>>>();

        const int rowCount = 5;
        var ids = new List<Guid>();
        for (var i = 0; i < rowCount; i++)
        {
            var result = await PersistResult(services, evaluator, EvaluationScore.Good);
            ids.Add(result.Id);
            await ClearStatRows(services, result.Id);
        }

        // batchSize 2 over 5 candidates = three iterations (2, 2, 1): exercises the keyset pagination
        // loop and the final-partial-batch termination a single-batch test never reaches.
        var backfill = new EvaluationStatBackfillService(
            ownedContextFactory, NullLogger<EvaluationStatBackfillService>.Instance, batchSize: 2);

        (await backfill.BackfillAsync(CancellationToken)).Should().Be(rowCount);

        foreach (var id in ids)
        {
            (await CountStatRows(services, id)).Should().Be(1);
        }
    }

    [TestMethod]
    public async Task Backfill_ResultWithNoEvaluations_IsSkippedAndTerminates()
    {
        IServiceProvider services = GetServices();

        // A test result that carries no evaluations can never gain a projection row, so it never leaves
        // the candidate set. The keyset cursor must step past it instead of looping on it forever.
        await PersistResult(services, evaluator: null, score: null);

        var backfill = services.GetRequiredService<EvaluationStatBackfillService>();
        (await backfill.BackfillAsync(CancellationToken)).Should().Be(0);
    }

    [TestMethod]
    public async Task StartAsync_WhenBackfillKeepsFailing_SwallowsAndDoesNotBlockBoot()
    {
        // Best-effort: a persistent failure must be logged, not thrown, or it would break host startup.
        Func<Owned<StorageDbContext>> throwingFactory = () => throw new InvalidOperationException("database unavailable");
        var backfill = new EvaluationStatBackfillService(
            throwingFactory, NullLogger<EvaluationStatBackfillService>.Instance, retryDelay: TimeSpan.Zero);

        var act = async () => await backfill.StartAsync(CancellationToken);

        await act.Should().NotThrowAsync();
    }

    // A window centred on now: domain factories stamp CreatedAt with the current time, so seeded
    // results fall inside it.
    private static (DateTimeOffset From, DateTimeOffset To) NowWindow()
        => (DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    private async Task<ITestResult> PersistResult(
        IServiceProvider services,
        IEvaluator? evaluator,
        EvaluationScore? score,
        TokenUsage? tokenUsage = null,
        decimal? cost = null)
    {
        var testCaseGen = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var completionGen = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();
        var resultFactory = services.GetRequiredService<ITestResult.CreateNew>();
        var repo = services.GetRequiredService<ITestResultRepository>();

        var testCase = await testCaseGen.CreateAsync(CancellationToken);
        var completion = await completionGen.CreateAsync(CancellationToken);

        IReadOnlyList<IEvaluation> evaluations = [];
        if (evaluator is not null && score is not null)
        {
            var evalFactory = services.GetRequiredService<IEvaluation.Create>();
            evaluations = [evalFactory(evaluator, score.Value, TimeSpan.FromMilliseconds(10), tokenUsage, cost, null)];
        }

        var result = resultFactory(testCase, completion, evaluations);
        return await repo.AddAsync(result, CancellationToken);
    }

    private static async Task ClearStatRows(IServiceProvider services, Guid testResultId)
    {
        var db = services.GetRequiredService<Func<StorageDbContext>>()();
        var rows = await db.Set<EvaluationStatEntity>().Where(e => e.TestResultId == testResultId).ToListAsync();
        db.Set<EvaluationStatEntity>().RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<List<EvaluationStatEntity>> StatRows(IServiceProvider services, Guid testResultId)
        => await services.GetRequiredService<Func<StorageDbContext>>()()
            .Set<EvaluationStatEntity>()
            .AsNoTracking()
            .Where(e => e.TestResultId == testResultId)
            .ToListAsync();

    private static async Task<int> CountStatRows(IServiceProvider services, Guid testResultId)
        => (await StatRows(services, testResultId)).Count;
}
