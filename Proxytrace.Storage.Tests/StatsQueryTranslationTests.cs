using Autofac;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Security;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.AgentVersion;
using Proxytrace.Storage.Internal.Entities.Session;
using Proxytrace.Storage.Internal.Entities.Statistics;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// Guards that the statistics aggregate shapes translate to server-side SQL on the PostgreSQL
/// provider. The behavioral tests all run on the in-memory provider, which happily client-evaluates
/// anything, so an aggregate silently falling back to client evaluation (an O(rows) materialization
/// at 1M+ rows — see docs/performance-testing.md) would never fail there. <c>ToQueryString()</c>
/// compiles the query against the real Npgsql provider without needing a live database and throws
/// when EF cannot translate, so these tests fail fast on a non-translatable shape.
/// <para>
/// The queries below intentionally mirror the aggregate <c>Select</c> shapes in
/// <c>AgentCallStatsQueries</c> / <c>TestRunStatsStore</c> — keep them in sync when those change.
/// </para>
/// </summary>
[TestClass]
public sealed class StatsQueryTranslationTests
{
    private static IContainer BuildPostgresContainer()
    {
        // Mirrors StorageDbContextFactory: a PostgreSQL-configured storage graph with the seams the
        // model-building path injects but never invokes stubbed out. The connection string is a
        // placeholder — ToQueryString compiles SQL without opening a connection.
        var builder = new ContainerBuilder();
        builder.RegisterModule(new Storage.Module(_ => StorageConfiguration.Postgres(
            "Host=localhost;Database=translation-check;Username=none;Password=none")));
        builder.RegisterStub<ISecretProtector>();
        builder.RegisterStub<ISecretHasher>();
        builder.RegisterStub<IAgentNameGenerator>();
        builder.RegisterStub<IProviderClient>();
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        return builder.Build();
    }

    [TestMethod]
    public void SummaryAggregate_WithNullAwareLatencyAverage_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetSummaryAsync / GetModelBreakdownAsync aggregate shape (issue #288 finding 1): the
        // latency mean sums/counts only non-null samples instead of averaging nulls as 0.
        string sql = context.Set<AgentCallEntity>()
            .GroupBy(c => c.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Count = (long)g.Count(),
                Input = g.Sum(c => (long?)c.InputTokens ?? 0L),
                LatencySum = g.Sum(c => c.LatencyMs ?? 0d),
                LatencyCount = g.Count(c => c.LatencyMs != null),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
    }

    [TestMethod]
    public void TestRunStatsPassTotals_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetPassTotalsAsync shape: one scalar row instead of materializing all run history.
        string sql = context.Set<TestRunStatsEntity>()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cases = g.Sum(e => e.TestCases),
                Passed = g.Sum(e => e.Passed),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
    }

    [TestMethod]
    public void TestRunStatsRecentCohorts_TranslatesToServerSideGroupByWithLimit()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetRecentCohortsAsync shape: cohort GROUP BY, ordered by latest completion, capped.
        string sql = context.Set<TestRunStatsEntity>()
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
            .Take(50)
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("LIMIT");
    }

    [TestMethod]
    public void AnomalyCountsAggregate_BitmaskSplitPerBucketPerAgent_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetAnomalyCountsByAgentAsync shape: outlier rows only (matches the partial index
        // filter), integer-slot day buckets per agent, and the static/custom bitmask split counts.
        const double widthMs = 24 * 60 * 60 * 1000d;
        const OutlierFlags staticBits =
            OutlierFlags.HighTokens | OutlierFlags.HighLatency | OutlierFlags.LowCacheHit | OutlierFlags.ManyToolCalls;
        string sql = context.Set<AgentCallEntity>()
            .Where(c => c.OutlierFlags != OutlierFlags.None)
            .Join(context.Set<AgentVersionEntity>(),
                c => c.AgentVersionId, v => v.Id,
                (c, v) => new { c.CreatedAt, v.AgentId, c.OutlierFlags })
            .GroupBy(x => new
            {
                Bucket = (int)Math.Floor((x.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                x.AgentId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.AgentId,
                Static = g.Count(x => (x.OutlierFlags & staticBits) != OutlierFlags.None),
                Custom = g.Count(x => (x.OutlierFlags & OutlierFlags.CustomAnomaly) != OutlierFlags.None),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("<> 0");
    }

    [TestMethod]
    public void AgentCallsListBySession_FiltersAndOrders_TranslatesToServerSideIndexedQuery()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The session-detail traces list (AgentCallFilter.SessionId): WHERE SessionId = @id
        // ORDER BY CreatedAt DESC LIMIT 50, served by the (SessionId, CreatedAt) index. Scalar
        // projection only — the Request/Response payload columns are never read, and the WHERE /
        // ORDER BY / LIMIT all translate to SQL rather than materializing the table client-side.
        Guid sessionId = Guid.NewGuid();
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(0)
            .Take(50)
            .Select(e => new { e.Id, e.CreatedAt, e.SessionId, e.HttpStatus })
            .ToQueryString();

        sql.Should().Contain("\"SessionId\" =");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [TestMethod]
    public void SessionsRecent_OrdersByLastActivity_TranslatesToServerSidePagedQuery()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetRecentAsync shape: WHERE ProjectId = @p ORDER BY LastActivityAt DESC LIMIT 50,
        // served by the descending (ProjectId, LastActivityAt) index. It never aggregates the
        // high-volume traces table — TraceCount/TotalTokens are denormalized onto the Sessions row.
        Guid projectId = Guid.NewGuid();
        string sql = context.Set<SessionEntity>()
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.LastActivityAt)
            .Skip(0)
            .Take(50)
            .ToQueryString();

        sql.Should().Contain("\"ProjectId\" =");
        sql.Should().Contain("ORDER BY");
        sql.Should().Contain("LIMIT");
    }

    [TestMethod]
    public void PulseAggregate_PerMinuteCountBuckets_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetPulseAsync shape: integer-slot minute buckets with a bare per-bucket count.
        DateTimeOffset from = DateTimeOffset.UtcNow.AddMinutes(-60);
        const double bucketMs = 60_000d;
        string sql = context.Set<AgentCallEntity>()
            .GroupBy(c => (int)Math.Floor((c.CreatedAt - from).TotalMilliseconds / bucketMs))
            .Select(g => new { Index = g.Key, Count = g.Count() })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
    }

    [TestMethod]
    public void CostByProjectAndAgentAggregate_JoinedOnAgentVersion_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetCostByProjectAndAgentAsync shape (the cost-budget guard's month-to-date input): an
        // AgentCall carries no project, so the grouping keys come from a join onto AgentVersion.
        // Only token sums cross the wire — pricing happens in C# — so the result is
        // O(projects × agents × endpoints), never O(calls).
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Join(
                context.Set<AgentVersionEntity>().AsNoTracking(),
                c => c.AgentVersionId,
                v => v.Id,
                (c, v) => new { Call = c, Version = v })
            .GroupBy(x => new { x.Version.Project, x.Version.AgentId, x.Call.EndpointId })
            .Select(g => new
            {
                g.Key.Project,
                g.Key.AgentId,
                g.Key.EndpointId,
                Input = g.Sum(x => (long?)x.Call.InputTokens ?? 0L),
                Output = g.Sum(x => (long?)x.Call.OutputTokens ?? 0L),
                Cached = g.Sum(x => (long?)x.Call.CachedInputTokens ?? 0L),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().ContainEquivalentOf("sum(");
    }

    [TestMethod]
    public void CostByApiKeyAggregate_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetCostByApiKeyAsync shape. ApiKeyId is a column of the call itself, but the project
        // still comes from the AgentVersion join. Grouping by a NULLABLE key must not push the
        // aggregate client-side — the null group is the unattributed remainder and has to be
        // computed in SQL like every other group.
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Join(
                context.Set<AgentVersionEntity>().AsNoTracking(),
                c => c.AgentVersionId,
                v => v.Id,
                (c, v) => new { Call = c, Version = v })
            .GroupBy(x => new { x.Version.Project, x.Call.ApiKeyId, x.Call.EndpointId })
            .Select(g => new
            {
                g.Key.Project,
                g.Key.ApiKeyId,
                g.Key.EndpointId,
                Input = g.Sum(x => (long?)x.Call.InputTokens ?? 0L),
                Output = g.Sum(x => (long?)x.Call.OutputTokens ?? 0L),
                Cached = g.Sum(x => (long?)x.Call.CachedInputTokens ?? 0L),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().ContainEquivalentOf("sum(");
    }

    [TestMethod]
    public void CostSeriesByApiKeyAggregate_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        double widthMs = TimeSpan.FromDays(1).TotalMilliseconds;

        // The GetCostSeriesByApiKeyAsync shape. Unlike the per-agent series this needs no join —
        // both grouping keys are columns of the call — so a regression that reintroduced one would
        // show up here as a changed plan, not just slower SQL.
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .GroupBy(c => new
            {
                Bucket = (int)Math.Floor((c.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                c.ApiKeyId,
                c.EndpointId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.ApiKeyId,
                g.Key.EndpointId,
                Input = g.Sum(x => (long?)x.InputTokens ?? 0L),
                Output = g.Sum(x => (long?)x.OutputTokens ?? 0L),
                Cached = g.Sum(x => (long?)x.CachedInputTokens ?? 0L),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().ContainEquivalentOf("sum(");
    }

    [TestMethod]
    public void CostSeriesByAgentAggregate_BucketedAndJoined_TranslatesToServerSideGroupBy()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The GetCostSeriesByAgentAsync shape: the same join, additionally keyed by the integer
        // epoch bucket so the cost-over-time chart stays O(buckets × agents × endpoints).
        const double widthMs = 86_400_000d;
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Join(
                context.Set<AgentVersionEntity>().AsNoTracking(),
                c => c.AgentVersionId,
                v => v.Id,
                (c, v) => new { Call = c, Version = v })
            .GroupBy(x => new
            {
                Bucket = (int)Math.Floor((x.Call.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                x.Version.AgentId,
                x.Call.EndpointId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.AgentId,
                g.Key.EndpointId,
                Input = g.Sum(x => (long?)x.Call.InputTokens ?? 0L),
                Output = g.Sum(x => (long?)x.Call.OutputTokens ?? 0L),
                Cached = g.Sum(x => (long?)x.Call.CachedInputTokens ?? 0L),
            })
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
    }

    [TestMethod]
    public void UnpricedEndpointProbe_DistinctThenAny_TranslatesToServerSideQuery()
    {
        using IContainer container = BuildPostgresContainer();
        var context = container.Resolve<StorageDbContext>();

        // The HasUnpricedEndpointsAsync shape: the window's DISTINCT endpoint ids (one row per
        // endpoint, not per call) then a single indexed price lookup.
        string sql = context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Select(c => c.EndpointId)
            .Distinct()
            .ToQueryString();

        sql.Should().Contain("DISTINCT");
    }
}
