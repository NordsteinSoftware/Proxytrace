using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluation;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.TestResult;

namespace Proxytrace.Storage.Internal.Entities.TestResult;

[UsedImplicitly]
internal class TestResultRepository : AbstractRepository<ITestResult, TestResultEntity>, ITestResultRepository
{
    // How many of this evaluator's rows to consider per requested result, so repeats of the same
    // test case (a suite re-run, or an A/B sample count) do not starve the de-duplicated output.
    private const int CandidateWindowPerResult = 20;
    private const int MaxCandidateWindow = 2_000;

    // The search path filters on text after mapping, so it needs a wider pool of distinct cases to
    // filter down from than the plain "recent" listing does.
    private const int SearchCandidateCases = 300;
    private const int SearchCandidateWindow = MaxCandidateWindow;

    private static int CandidateWindow(int count)
        => Math.Min(MaxCandidateWindow, Math.Max(count, count * CandidateWindowPerResult));

    /// <summary>
    /// Initializes a new instance of the <see cref="TestResultRepository"/> class.
    /// </summary>
    public TestResultRepository(
        IMapper<ITestResult, TestResultEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    // The EvaluationStat projection rows copy the parent's CreatedAt at write time, but the base
    // update only copies scalar columns onto the tracked row — the projection children were left
    // untouched, so an update that rewrites CreatedAt (the demo seed's statistics backdating) kept
    // the stat rows at their original timestamps and the evaluator-stats queries bucketed on stale
    // times. Rebuild the projection from the freshly mapped entity so it always mirrors the parent.
    protected override async Task UpdateRelationsAsync(
        DbContext context,
        TestResultEntity storedEntity,
        CancellationToken cancellationToken)
    {
        var stale = await context.Set<EvaluationStatEntity>()
            .Where(e => e.TestResultId == storedEntity.Id)
            .ToListAsync(cancellationToken);
        context.Set<EvaluationStatEntity>().RemoveRange(stale);
        context.Set<EvaluationStatEntity>().AddRange(storedEntity.EvaluationStats);
    }

    /// <summary>
    /// Gets the latest by test case asynchronously.
    /// </summary>
    public async Task<ITestResult?> GetLatestByTestCaseAsync(Guid testCaseId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context
            .Set<TestResultEntity>()
            .AsNoTracking()
            .Where(r => r.TestCase == testCaseId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return await Map(stored, cancellationToken);
    }

    /// <summary>
    /// Gets the latest by evaluator asynchronously.
    /// </summary>
    public async Task<ITestResult?> GetLatestByEvaluatorAsync(Guid evaluatorId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();

        // Exact, and independent of how many results exist elsewhere: the EvaluationStat projection
        // is filtered by evaluator in SQL over its (EvaluatorId, CreatedAt) index, so this reads the
        // single newest row for this evaluator. The previous implementation scanned the 200 most
        // recent results across the whole install and filtered afterwards, so an evaluator whose
        // latest result had fallen outside that global window simply reported nothing.
        var matchId = await EvaluatorStatsQuery(context, evaluatorId)
            .Select(s => (Guid?)s.TestResultId)
            .FirstOrDefaultAsync(cancellationToken);
        if (matchId is null) return null;

        var entity = await context
            .Set<TestResultEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == matchId.Value, cancellationToken);
        return await Map(entity, cancellationToken);
    }

    /// <summary>
    /// Gets the recent by evaluator asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestResult>> GetRecentByEvaluatorAsync(
        Guid evaluatorId,
        int count,
        EvaluationScore? score = null,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) return [];

        var context = contextFactory();
        var matchingIds = await DedupedRecentIdsAsync(
            context, evaluatorId, score, count, CandidateWindow(count), cancellationToken);

        return await LoadFullInOrderAsync(context, matchingIds, cancellationToken);
    }

    /// <summary>
    /// Searches the by evaluator asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ITestResult>> SearchByEvaluatorAsync(
        Guid evaluatorId,
        string query,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0) return [];

        // Mirrors GetRecentByEvaluatorAsync: the evaluator filter runs in SQL, then the text filter
        // runs after mapping (Summary is computed by the domain entity, so it cannot be pushed down).
        var context = contextFactory();
        var dedupedIds = await DedupedRecentIdsAsync(
            context, evaluatorId, score: null, take: SearchCandidateCases,
            candidateWindow: SearchCandidateWindow, cancellationToken);
        if (dedupedIds.Count == 0) return [];

        // Load the deduped candidates' full rows once, then map+filter until count is reached.
        // Summary is computed by the domain entity, so the text filter runs after mapping.
        var byId = (await context
                .Set<TestResultEntity>()
                .AsNoTracking()
                .Where(r => dedupedIds.Contains(r.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(e => e.Id);

        var trimmed = query.Trim();
        var matches = new List<ITestResult>();
        foreach (var id in dedupedIds)
        {
            if (!byId.TryGetValue(id, out var entity)) continue;
            var mapped = await Map(entity, cancellationToken);
            if (mapped is null) continue;
            if (trimmed.Length > 0 && !MatchesQuery(mapped, evaluatorId, trimmed)) continue;
            matches.Add(mapped);
            if (matches.Count >= count) break;
        }
        return matches;
    }

    /// <summary>
    /// Newest-first <see cref="EvaluationStatEntity"/> rows for one evaluator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the fix for the evaluator-history queries. They used to take the most recent N test
    /// results <b>across the whole installation</b> and filter by evaluator in memory afterwards, so
    /// once more than N results existed anywhere, an evaluator whose results fell outside that
    /// global window silently returned nothing — and it was cross-tenant: a busy project's runs
    /// evicted everyone else's. The filter now runs in SQL, over the
    /// <c>(EvaluatorId, CreatedAt)</c> index on the projection table, so what a caller sees depends
    /// only on their own evaluator's history.
    /// </para>
    /// <para>
    /// <see cref="EvaluationStatEntity"/> exists precisely for this: it is the queryable projection
    /// of the evaluations that are otherwise only reachable inside a JSON column, and
    /// <c>EvaluationStatBackfillService</c> populates it for results written before it existed.
    /// </para>
    /// </remarks>
    private static IQueryable<EvaluationStatEntity> EvaluatorStatsQuery(
        DbContext context,
        Guid evaluatorId,
        EvaluationScore? score = null)
    {
        var query = context
            .Set<EvaluationStatEntity>()
            .AsNoTracking()
            .Where(s => s.EvaluatorId == evaluatorId);

        if (score is not null)
        {
            query = query.Where(s => s.Score == score);
        }

        // Id as the tiebreak so results sharing a CreatedAt (a run's cases are written together, and
        // the timestamp truncates to microseconds) keep a stable total order rather than an
        // arbitrary one that could drop or duplicate a row at the window boundary.
        return query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id);
    }

    /// <summary>
    /// Ids of the most recent test results for one evaluator, at most one per test case, newest
    /// first.
    /// </summary>
    /// <remarks>
    /// De-duplication by test case still happens in memory, because the column it keys on lives on
    /// the parent result rather than the projection row. It is bounded by
    /// <paramref name="candidateWindow"/> — a window over <b>this evaluator's</b> rows, not over the
    /// whole table, so it cannot be exhausted by unrelated activity. A window smaller than the
    /// number of repeats of a single case is the only way to under-fill the result, which is why it
    /// scales with what the caller asked for.
    /// </remarks>
    private static async Task<IReadOnlyList<Guid>> DedupedRecentIdsAsync(
        DbContext context,
        Guid evaluatorId,
        EvaluationScore? score,
        int take,
        int candidateWindow,
        CancellationToken cancellationToken)
    {
        var candidateIds = await EvaluatorStatsQuery(context, evaluatorId, score)
            .Select(s => s.TestResultId)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);
        if (candidateIds.Count == 0) return [];

        // Reads only the two columns the de-duplication needs — never the large ActualResponse
        // payload, which the caller loads afterwards for the rows it actually keeps.
        var candidates = await context
            .Set<TestResultEntity>()
            .AsNoTracking()
            .Where(r => candidateIds.Contains(r.Id))
            .Select(r => new { r.Id, r.TestCase, r.CreatedAt })
            .ToListAsync(cancellationToken);

        var byId = candidates.ToDictionary(c => c.Id);
        var seenCases = new HashSet<Guid>();
        var result = new List<Guid>(Math.Min(take, candidateIds.Count));

        // Walk in the candidate order, which is the SQL ordering — newest first.
        foreach (var id in candidateIds)
        {
            if (!byId.TryGetValue(id, out var candidate)) continue;
            if (!seenCases.Add(candidate.TestCase)) continue;
            result.Add(candidate.Id);
            if (result.Count >= take) break;
        }

        return result;
    }

    // Loads the full rows for the given ids and maps them, preserving the order of <paramref name="ids"/>.
    private async Task<IReadOnlyList<ITestResult>> LoadFullInOrderAsync(
        DbContext context,
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];

        var byId = (await context
                .Set<TestResultEntity>()
                .AsNoTracking()
                .Where(r => ids.Contains(r.Id))
                .ToListAsync(cancellationToken))
            .ToDictionary(e => e.Id);

        var mapped = new List<ITestResult>(ids.Count);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var entity)) continue;
            var m = await Map(entity, cancellationToken);
            if (m is not null) mapped.Add(m);
        }
        return mapped;
    }

    private static bool MatchesQuery(ITestResult result, Guid evaluatorId, string query)
    {
        if (result.TestCase.GetSummary().Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        var reasoning = result.Evaluations
            .FirstOrDefault(e => e.Evaluator.Id == evaluatorId)?.Reasoning;
        return reasoning is not null && reasoning.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
