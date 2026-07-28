using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Evaluation;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.Usage;
using Proxytrace.Storage.Internal.Entities.Evaluator;
using Proxytrace.Storage.Internal.Entities.TestResult;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class EvaluatorStatsQueriesTests : BaseTest<Module>
{
    private static readonly DateTimeOffset From = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(30);

    [TestMethod]
    public async Task GetOverview_NoData_ReturnsZeroedStats()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();

        var result = await reader.GetOverviewAsync(Guid.NewGuid(), From, To, StatisticsBucket.Daily, CancellationToken);

        result.Summary.TotalEvaluations.Should().Be(0);
        result.PassRateTrend.Should().BeEmpty();
        result.ScoreDistribution.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetSparklines_NoEvaluatorsInProject_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();

        var result = await reader.GetSparklinesAsync([Guid.NewGuid()], From, To, StatisticsBucket.Daily, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetOverview_WithEvaluations_AggregatesOnlyTheRequestedEvaluator()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var subject = await generator.CreateAsync(CancellationToken);
        var other = await generator.CreateAsync(CancellationToken);

        await PersistResult(services, subject, EvaluationScore.Good);
        await PersistResult(services, subject, EvaluationScore.Excellent);
        await PersistResult(services, subject, EvaluationScore.Bad);
        // A different evaluator's evaluation must not bleed into the subject's overview — the bug
        // this projection fixes was that scope was applied in memory rather than in SQL.
        await PersistResult(services, other, EvaluationScore.Terrible);

        var (from, to) = NowWindow();
        var result = await reader.GetOverviewAsync(subject.Id, from, to, StatisticsBucket.Daily, CancellationToken);

        result.Summary.TotalEvaluations.Should().Be(3);
        result.Summary.AvgScore.Should().BeApproximately((4 + 5 + 2) / 3.0, 0.0001);
        result.Summary.OverallPassRate.Should().BeApproximately(2 / 3.0, 0.0001);
        result.ScoreDistribution.Sum(b => b.Count).Should().Be(3);
        result.ScoreDistribution.Should().NotContain(b => b.Score == EvaluationScore.Terrible.ToString());
    }

    [TestMethod]
    public async Task GetSparklines_WithEvaluations_ReturnsPassRatePointsForProjectEvaluators()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);

        await PersistResult(services, evaluator, EvaluationScore.Good);
        await PersistResult(services, evaluator, EvaluationScore.Bad);

        Guid projectId = await services.GetRequiredService<Func<StorageDbContext>>()()
            .Set<EvaluatorEntity>()
            .Where(e => e.Id == evaluator.Id)
            .Select(e => e.Project)
            .FirstAsync(CancellationToken);

        var (from, to) = NowWindow();
        var result = await reader.GetSparklinesAsync([projectId], from, to, StatisticsBucket.Daily, CancellationToken);

        var sparkline = result.Should().ContainSingle(s => s.EvaluatorId == evaluator.Id).Subject;
        sparkline.Points.Sum(p => p.Total).Should().Be(2);
        sparkline.Points.Sum(p => p.Passed).Should().Be(1);
    }

    [TestMethod]
    public async Task GetSparklines_WithSeveralProjects_CoversEveryScopedProjectAndNothingElse()
    {
        IServiceProvider services = GetServices();
        var reader = services.GetRequiredService<IEvaluatorStatsReader>();

        // A caller who may read two projects and filtered by none (#483): before the reader took a
        // set, the overview could only sparkline one project, so this caller saw none at all.
        var (firstProject, first) = await CreateEvaluatorInNewProject(services);
        var (secondProject, second) = await CreateEvaluatorInNewProject(services);
        var (_, outsider) = await CreateEvaluatorInNewProject(services);

        await PersistResult(services, first, EvaluationScore.Good);
        await PersistResult(services, second, EvaluationScore.Bad);
        await PersistResult(services, outsider, EvaluationScore.Excellent);

        var (from, to) = NowWindow();
        var result = await reader.GetSparklinesAsync(
            [firstProject, secondProject], from, to, StatisticsBucket.Daily, CancellationToken);

        result.Select(s => s.EvaluatorId).Should().BeEquivalentTo(new[] { first.Id, second.Id });
    }

    private async Task<(Guid ProjectId, IEvaluator Evaluator)> CreateEvaluatorInNewProject(IServiceProvider services)
    {
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var evaluator = await services.GetRequiredService<IRepository<IEvaluator>>().AddAsync(
            services.GetRequiredService<IExactMatchEvaluator.CreateNew>()(project),
            CancellationToken);
        return (project.Id, evaluator);
    }

    [TestMethod]
    public async Task AddResult_PopulatesEvaluationStatProjection()
    {
        IServiceProvider services = GetServices();
        var generator = services.GetRequiredService<IDomainEntityGenerator<IEvaluator>>();
        var evaluator = await generator.CreateAsync(CancellationToken);

        var usage = new TokenUsage(11, 22, 3);
        var persisted = await PersistResult(services, evaluator, EvaluationScore.Good, usage, cost: 0.5m);

        var rows = await services.GetRequiredService<Func<StorageDbContext>>()()
            .Set<EvaluationStatEntity>()
            .Where(e => e.TestResultId == persisted.Id)
            .ToListAsync(CancellationToken);

        var row = rows.Should().ContainSingle().Subject;
        row.EvaluatorId.Should().Be(evaluator.Id);
        row.CreatedAt.Should().Be(persisted.CreatedAt);
        row.Score.Should().Be(EvaluationScore.Good);
        row.HasError.Should().BeFalse();
        row.InputTokens.Should().Be(11);
        row.OutputTokens.Should().Be(22);
        row.CachedInputTokens.Should().Be(3);
        row.Cost.Should().Be(0.5m);
    }

    // A window centred on now: domain factories stamp CreatedAt with the current time, so the
    // seeded results fall inside it (unlike the fixed-date From/To used by the no-data cases).
    private static (DateTimeOffset From, DateTimeOffset To) NowWindow()
        => (DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

    private async Task<ITestResult> PersistResult(
        IServiceProvider services,
        IEvaluator evaluator,
        EvaluationScore score,
        TokenUsage? tokenUsage = null,
        decimal? cost = null)
    {
        var testCaseGen = services.GetRequiredService<IDomainEntityGenerator<ITestCase>>();
        var completionGen = services.GetRequiredService<IDomainObjectGenerator<ICompletion>>();
        var evalFactory = services.GetRequiredService<IEvaluation.Create>();
        var resultFactory = services.GetRequiredService<ITestResult.CreateNew>();
        var repo = services.GetRequiredService<ITestResultRepository>();

        var testCase = await testCaseGen.CreateAsync(CancellationToken);
        var completion = await completionGen.CreateAsync(CancellationToken);
        var evaluation = evalFactory(evaluator, score, TimeSpan.FromMilliseconds(10), tokenUsage, cost, null);
        var result = resultFactory(testCase, completion, [evaluation]);
        return await repo.AddAsync(result, CancellationToken);
    }
}
