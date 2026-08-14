using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Storage.Internal;
using Proxytrace.Storage.Internal.Entities.TestResult;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class TestResultRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetRecentByEvaluator_WithScoreFilter_ReturnsOnlyMatchingScore()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good);
        await PersistResult(services, evaluator, EvaluationScore.Bad);

        var good = await repo.GetRecentByEvaluatorAsync(evaluator.Id, 20, EvaluationScore.Good, CancellationToken);
        var bad = await repo.GetRecentByEvaluatorAsync(evaluator.Id, 20, EvaluationScore.Bad, CancellationToken);

        good.Should().ContainSingle();
        good.Single().Evaluations.Should().Contain(e => e.Evaluator.Id == evaluator.Id && e.Score == EvaluationScore.Good);
        bad.Should().ContainSingle();
        bad.Single().Evaluations.Should().Contain(e => e.Evaluator.Id == evaluator.Id && e.Score == EvaluationScore.Bad);
    }

    [TestMethod]
    public async Task GetRecentByEvaluator_WithoutScoreFilter_ReturnsAllScores()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good);
        await PersistResult(services, evaluator, EvaluationScore.Bad);

        var all = await repo.GetRecentByEvaluatorAsync(evaluator.Id, 20, cancellationToken: CancellationToken);

        all.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task SearchByEvaluator_MatchesReasoning_ReturnsOnlyMatchingCase()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        var matched = await PersistResult(services, evaluator, EvaluationScore.Good, "alpha unique reasoning");
        await PersistResult(services, evaluator, EvaluationScore.Bad, "beta different reasoning");

        var hits = await repo.SearchByEvaluatorAsync(evaluator.Id, "alpha", 20, CancellationToken);

        hits.Should().ContainSingle();
        hits.Single().TestCase.Id.Should().Be(matched.Id);
    }

    [TestMethod]
    public async Task SearchByEvaluator_EmptyQuery_ReturnsAllMatchesForEvaluator()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good, "anything");
        await PersistResult(services, evaluator, EvaluationScore.Bad, "anything else");

        var hits = await repo.SearchByEvaluatorAsync(evaluator.Id, "", 20, CancellationToken);

        hits.Should().HaveCount(2);
    }

    [TestMethod]
    public async Task GetRecentByEvaluator_AfterEvaluatorArchived_StillResolvesHistory()
    {
        // Archiving (soft-delete) keeps the evaluator row, so a prior test result's evaluation —
        // which live-fetches the evaluator by id at map time — must still load.
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var evaluatorRepo = services.GetRequiredService<IEvaluatorRepository>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good, "historical reasoning");

        await evaluatorRepo.ArchiveAsync(evaluator.Id, CancellationToken);

        var results = await repo.GetRecentByEvaluatorAsync(evaluator.Id, 20, cancellationToken: CancellationToken);
        results.Should().ContainSingle();
        results.Single().Evaluations.Should().Contain(e => e.Evaluator.Id == evaluator.Id);
    }

    [TestMethod]
    public async Task UpdateAsync_BackdatedCreatedAt_RewritesEvaluationStatRows()
    {
        IServiceProvider services = GetServices();
        var repo = services.GetRequiredService<ITestResultRepository>();
        var createExisting = services.GetRequiredService<ITestResult.CreateExisting>();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>().GetOrCreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good);
        var result = (await repo.GetRecentByEvaluatorAsync(evaluator.Id, 1, cancellationToken: CancellationToken)).Single();

        // The demo seed's statistics backdating rewrites a persisted result's CreatedAt via
        // CreateExisting + UpdateAsync; the EvaluationStat projection rows must follow, or the
        // evaluator-stats queries keep bucketing the evaluation at the original write time.
        var backdated = DateTimeOffset.UtcNow.AddDays(-14);
        var updated = createExisting(
            testCase: result.TestCase,
            actualResponse: result.ActualResponse,
            evaluations: result.Evaluations,
            latency: result.Latency,
            usage: result.Usage,
            existing: new BackdatedData(result.Id, backdated, result.UpdatedAt));
        await repo.UpdateAsync(updated, CancellationToken);

        var rows = await services.GetRequiredService<Func<StorageDbContext>>()()
            .Set<EvaluationStatEntity>()
            .AsNoTracking()
            .Where(e => e.TestResultId == result.Id)
            .ToListAsync(CancellationToken);
        var row = rows.Should().ContainSingle().Subject;
        row.CreatedAt.Should().Be(backdated);
        row.EvaluatorId.Should().Be(evaluator.Id);
        row.Score.Should().Be(EvaluationScore.Good);
    }

    [TestMethod]
    public async Task GetRecentByEvaluator_WhenOtherEvaluatorsHaveManyNewerResults_StillFindsItsOwn()
    {
        // The window used to be the most recent N results across the WHOLE install, filtered by
        // evaluator only afterwards. Once anything else produced N newer results, an evaluator's
        // own history silently returned empty — and it was cross-tenant: a busy project's runs
        // evicted everyone else's. Filtering by evaluator in SQL makes the answer depend only on
        // this evaluator's own history.
        var services = GetServices();
        var evaluatorGen = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var repo = services.GetRequiredService<ITestResultRepository>();

        var mine = await evaluatorGen.CreateAsync(CancellationToken);
        var noisy = await evaluatorGen.CreateAsync(CancellationToken);

        await PersistResult(services, mine, EvaluationScore.Good);

        // Bury it under far more recent results belonging to a different evaluator than any
        // plausible global window would have held.
        for (var i = 0; i < 250; i++)
        {
            await PersistResult(services, noisy, EvaluationScore.Good);
        }

        var recent = await repo.GetRecentByEvaluatorAsync(mine.Id, count: 10, cancellationToken: CancellationToken);
        var latest = await repo.GetLatestByEvaluatorAsync(mine.Id, CancellationToken);

        recent.Should().ContainSingle("this evaluator has exactly one result, however much else ran since");
        latest.Should().NotBeNull();
        latest?.Evaluations.Should().Contain(e => e.Evaluator.Id == mine.Id);
    }

    [TestMethod]
    public async Task SearchByEvaluator_WhenOtherEvaluatorsHaveManyNewerResults_StillFindsItsOwn()
    {
        var services = GetServices();
        var evaluatorGen = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var repo = services.GetRequiredService<ITestResultRepository>();

        var mine = await evaluatorGen.CreateAsync(CancellationToken);
        var noisy = await evaluatorGen.CreateAsync(CancellationToken);

        await PersistResult(services, mine, EvaluationScore.Bad, reasoning: "needle-in-a-haystack");

        for (var i = 0; i < 250; i++)
        {
            await PersistResult(services, noisy, EvaluationScore.Good, reasoning: "unrelated");
        }

        var matches = await repo.SearchByEvaluatorAsync(mine.Id, "needle-in-a-haystack", count: 10, CancellationToken);

        matches.Should().ContainSingle();
    }

    [TestMethod]
    public async Task GetRecentByEvaluator_WithAScoreFilter_ReturnsOnlyMatchingResults()
    {
        // The score filter moved into SQL alongside the evaluator filter — assert it still holds.
        var services = GetServices();
        var evaluator = await services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>()
            .CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<ITestResultRepository>();

        await PersistResult(services, evaluator, EvaluationScore.Good);
        await PersistResult(services, evaluator, EvaluationScore.Bad);
        await PersistResult(services, evaluator, EvaluationScore.Bad);

        var bad = await repo.GetRecentByEvaluatorAsync(
            evaluator.Id, count: 10, score: EvaluationScore.Bad, cancellationToken: CancellationToken);

        bad.Should().HaveCount(2);
        bad.Should().OnlyContain(r => r.Evaluations.Any(e => e.Score == EvaluationScore.Bad));
    }

    private sealed record BackdatedData(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt) : IDomainEntityData;

    private async Task<ITestCase> PersistResult(
        IServiceProvider services, IEvaluator evaluator, EvaluationScore score, string? reasoning = null)
    {
        var testCaseGen = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var completionGen = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();
        var evalFactory = services.GetRequiredService<IEvaluation.Create>();
        var resultFactory = services.GetRequiredService<ITestResult.CreateNew>();
        var repo = services.GetRequiredService<ITestResultRepository>();

        // Each result needs its own test case — GetRecentByEvaluatorAsync dedupes by test case.
        var testCase = await testCaseGen.CreateAsync(CancellationToken);
        var completion = await completionGen.CreateAsync(CancellationToken);
        var evaluation = evalFactory(evaluator, score, TimeSpan.FromMilliseconds(10), null, null, reasoning);
        var result = resultFactory(testCase, completion, [evaluation]);
        await repo.AddAsync(result, CancellationToken);
        return testCase;
    }
}
