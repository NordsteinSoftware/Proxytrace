using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.Statistics.TestRun;
using Proxytrace.Domain;
using Nordstein.Core.AI.Completions;
using Proxytrace.Storage.Internal.Entities.Statistics;

namespace Proxytrace.Storage.Internal.Statistics;

[UsedImplicitly]
internal class TestRunStatsStore : IStatsReader<TestRunStats, TestRunStats.Filter>, ITestRunStatsReader, IStatsWriter<TestRunStats>
{
    private readonly Func<StorageDbContext> contextFactory;
    private readonly ITransaction transaction;

    public TestRunStatsStore(Func<StorageDbContext> contextFactory, ITransaction transaction)
    {
        this.contextFactory = contextFactory;
        this.transaction = transaction;
    }

    public Task UpsertAsync(TestRunStats stats, CancellationToken cancellationToken = default)
        => transaction.InvokeAsync(async () =>
        {
            try
            {
                await UpsertCoreAsync(stats, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost an insert race with a concurrent projector run. Retry as update if the
                // row now exists; otherwise the failure is real — rethrow.
                if (!await ExistsAsync(stats.TestRunId, cancellationToken))
                {
                    throw;
                }
                await UpsertCoreAsync(stats, cancellationToken);
            }
        });

    private async Task UpsertCoreAsync(TestRunStats stats, CancellationToken cancellationToken)
    {
        StorageDbContext context = contextFactory();
        DbSet<TestRunStatsEntity> set = context.Set<TestRunStatsEntity>();

        TestRunStatsEntity? existing = await set
            .FirstOrDefaultAsync(e => e.TestRunId == stats.TestRunId, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            TestRunStatsEntity entity = ToEntity(stats, id: Guid.NewGuid(), createdAt: now, updatedAt: now);
            var entry = set.Add(entity);
            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // A failed SaveChanges leaves the insert tracked as Added. The ambient context is
                // shared with the retry in UpsertAsync, so detach the orphan here — otherwise the
                // retry's SaveChanges replays this insert and re-hits the unique TestRunId
                // constraint, turning a recoverable insert race into a hard failure.
                entry.State = EntityState.Detached;
                throw;
            }

            return;
        }

        TestRunStatsEntity updated = ToEntity(stats, id: existing.Id, createdAt: existing.CreatedAt, updatedAt: now);
        var existingEntry = context.Entry(existing);
        existingEntry.CurrentValues.SetValues(updated);
        // UpdatedAt is a concurrency token. The stats projector shares the ambient context across the
        // insert-race retry, so a row inserted earlier in this context is still tracked at .NET's
        // 100ns precision; realign the token's original to the microseconds PostgreSQL persists so the
        // `WHERE UpdatedAt = @original` check does not spuriously fail. See ConcurrencyTokenExtensions.
        // Only the relational provider truncates UpdatedAt on persist; the in-memory provider stores the
        // full 100ns value verbatim, so truncating the original there would make EF's own concurrency
        // check throw on every ordinary update — skip the realignment on non-relational providers.
        if (context.Database.IsRelational())
        {
            var updatedAtProperty = existingEntry.Property(e => e.UpdatedAt);
            updatedAtProperty.OriginalValue = updatedAtProperty.OriginalValue.TruncateToMicroseconds();
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<bool> ExistsAsync(Guid testRunId, CancellationToken cancellationToken)
        => contextFactory().Set<TestRunStatsEntity>()
            .AsNoTracking()
            .AnyAsync(e => e.TestRunId == testRunId, cancellationToken);

    public Task RemoveAsync(Guid testRunId, CancellationToken cancellationToken = default)
        => transaction.InvokeAsync(async () =>
        {
            StorageDbContext context = contextFactory();
            DbSet<TestRunStatsEntity> set = context.Set<TestRunStatsEntity>();
            TestRunStatsEntity? existing = await set
                .FirstOrDefaultAsync(e => e.TestRunId == testRunId, cancellationToken);
            if (existing is null)
            {
                return;
            }
            set.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        });

    public async Task<TestRunStats?> FindAsync(Guid testRunId, CancellationToken cancellationToken = default)
    {
        TestRunStatsEntity? entity = await contextFactory()
            .Set<TestRunStatsEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.TestRunId == testRunId, cancellationToken);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IReadOnlyList<TestRunStats>> QueryAsync(TestRunStats.Filter filter, CancellationToken cancellationToken = default)
    {
        IQueryable<TestRunStatsEntity> q = Query(contextFactory(), filter);

        List<TestRunStatsEntity> rows = await q.ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToArray();
    }

    public async Task<TestRunPassTotals> GetPassTotalsAsync(TestRunStats.Filter filter, CancellationToken cancellationToken = default)
    {
        // A single scalar aggregate row crosses the wire — never the O(all-history) row set the
        // generic QueryAsync would materialize just to sum two columns.
        var agg = await Query(contextFactory(), filter)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cases = g.Sum(e => e.TestCases),
                Passed = g.Sum(e => e.Passed),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new TestRunPassTotals(TotalCases: agg?.Cases ?? 0, TotalPassed: agg?.Passed ?? 0);
    }

    public async Task<IReadOnlyList<TestRunCohort>> GetRecentCohortsAsync(TestRunStats.Filter filter, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Max(limit, 1);

        // Collapse the sample dimension server-side — one row per (GroupId, EndpointId) cohort —
        // and keep only the `limit` most recently completed cohorts, so the result is bounded no
        // matter how much run history has accumulated. Rounded-mean Passed mirrors
        // TestRunStatsCohortExtensions.AggregateSamples (see the regression test comparing the two).
        var rows = await Query(contextFactory(), filter)
            .GroupBy(e => new { e.GroupId, e.EndpointId })
            .Select(g => new
            {
                g.Key.GroupId,
                g.Key.EndpointId,
                TestCases = g.Max(e => e.TestCases),
                PassedSum = g.Sum(e => e.Passed),
                SampleCount = g.Count(),
                LastRunCompletedAt = g.Max(e => e.RunCompletedAt),
            })
            .OrderByDescending(x => x.LastRunCompletedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.LastRunCompletedAt)
            .Select(x => new TestRunCohort(
                GroupId: x.GroupId,
                EndpointId: x.EndpointId,
                TestCases: x.TestCases,
                Passed: (int)Math.Round(x.PassedSum / (double)x.SampleCount),
                LastRunCompletedAt: x.LastRunCompletedAt))
            .ToArray();
    }

    private static IQueryable<TestRunStatsEntity> Query(StorageDbContext context, TestRunStats.Filter filter)
    {
        IQueryable<TestRunStatsEntity> q = context
            .Set<TestRunStatsEntity>()
            .AsNoTracking();

        if (filter.AgentId.HasValue)
        {
            q = q.Where(e => e.AgentId == filter.AgentId.Value);
        }
        // `null` means "no agent filter"; an EMPTY collection means "no agent matches" and must
        // narrow to nothing. Treating empty as unfiltered leaked global figures: a project with no
        // agents yet (or all archived) produced an empty AgentIds list, and the dashboard's
        // OverallPassRate and pass-rate sparkline were then computed over every other project's runs.
        if (filter.AgentIds is { } agentIds)
        {
            q = q.Where(e => agentIds.Contains(e.AgentId));
        }
        if (filter.EndpointId.HasValue)
        {
            q = q.Where(e => e.EndpointId == filter.EndpointId.Value);
        }
        if (filter.GroupId.HasValue)
        {
            q = q.Where(e => e.GroupId == filter.GroupId.Value);
        }
        if (filter.SuiteId.HasValue)
        {
            q = q.Where(e => e.SuiteId == filter.SuiteId.Value);
        }
        // Same null-vs-empty distinction as AgentIds above.
        if (filter.SuiteIds is { } suiteIds)
        {
            q = q.Where(e => suiteIds.Contains(e.SuiteId));
        }
        if (filter.From.HasValue)
        {
            q = q.Where(e => e.RunCompletedAt >= filter.From.Value);
        }
        if (filter.To.HasValue)
        {
            q = q.Where(e => e.RunCompletedAt <= filter.To.Value);
        }

        return q;
    }

    private static TestRunStatsEntity ToEntity(TestRunStats stats, Guid id, DateTimeOffset createdAt, DateTimeOffset updatedAt)
        => new()
        {
            Id = id,
            TestRunId = stats.TestRunId,
            AgentId = stats.AgentId,
            EndpointId = stats.EndpointId,
            GroupId = stats.GroupId,
            SuiteId = stats.SuiteId,
            TestCases = stats.TestCases,
            Passed = stats.Passed,
            InputTokens = (long?)stats.Usage?.InputTokenCount,
            OutputTokens = (long?)stats.Usage?.OutputTokenCount,
            CachedInputTokens = (long?)stats.Usage?.CachedInputTokenCount,
            TotalDurationMicroseconds = stats.TotalDuration.HasValue ? (long?)stats.TotalDuration.Value.TotalMicroseconds : null,
            Cost = stats.Cost,
            RunCompletedAt = stats.RunCompletedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };

    private static TestRunStats ToDto(TestRunStatsEntity entity)
    {
        TokenUsage? usage = entity.InputTokens.HasValue && entity.OutputTokens.HasValue
            ? new TokenUsage((ulong)entity.InputTokens.Value, (ulong)entity.OutputTokens.Value, (ulong)(entity.CachedInputTokens ?? 0))
            : null;

        TimeSpan? duration = entity.TotalDurationMicroseconds.HasValue
            ? TimeSpan.FromMicroseconds(entity.TotalDurationMicroseconds.Value)
            : null;

        return new TestRunStats(
            TestRunId: entity.TestRunId,
            AgentId: entity.AgentId,
            EndpointId: entity.EndpointId,
            GroupId: entity.GroupId,
            SuiteId: entity.SuiteId,
            TestCases: entity.TestCases,
            Passed: entity.Passed,
            TotalDuration: duration,
            Usage: usage,
            Cost: entity.Cost,
            RunCompletedAt: entity.RunCompletedAt);
    }
}
