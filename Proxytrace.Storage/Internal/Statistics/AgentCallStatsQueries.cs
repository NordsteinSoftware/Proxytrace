using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.AI.Completions;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.AgentCall;
using Proxytrace.Storage.Internal.Entities.AgentVersion;
using Proxytrace.Storage.Internal.Entities.Model;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Statistics;

[UsedImplicitly]
internal class AgentCallStatsQueries : IAgentCallStatsReader
{
    /// <summary>
    /// Upper bound on the per-call rows <see cref="GetAgentDistributionsAsync"/> pulls into memory.
    /// </summary>
    /// <remarks>
    /// The distributions are computed in C# (std-dev, and cost-per-conversation needs endpoint
    /// pricing), so the rows have to be materialized. The window is caller-supplied and unbounded,
    /// so without a cap a request spanning the whole history of a busy agent materializes every one
    /// of its traces — a memory spike that grows with the trace table. The cap keeps that bounded;
    /// distributions are statistical summaries, so the most recent N calls describe the shape of the
    /// data. When the cap bites it is logged rather than silently applied.
    /// </remarks>
    private const int MaxDistributionSamples = 200_000;

    private readonly Func<StorageDbContext> contextFactory;
    private readonly IMapper<IModelEndpoint, ModelEndpointEntity> endpointMapper;
    private readonly ILogger<AgentCallStatsQueries> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentCallStatsQueries"/> class.
    /// </summary>
    public AgentCallStatsQueries(
        Func<StorageDbContext> contextFactory,
        IMapper<IModelEndpoint, ModelEndpointEntity> endpointMapper,
        ILogger<AgentCallStatsQueries> logger)
    {
        this.contextFactory = contextFactory;
        this.endpointMapper = endpointMapper;
        this.logger = logger;
    }

    /// <summary>
    /// Gets the summary asynchronously.
    /// </summary>
    public async Task<StatisticsSummary> GetSummaryAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        // Latency averages over the non-null samples only (Sum / Count(!= null), the same pattern as
        // GetCallTrendsAsync) — Average(c => c.LatencyMs ?? 0d) would count latency-less calls as 0ms
        // and bias the mean low. Both aggregates translate server-side.
        var agg = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = (long)g.Count(),
                Input = g.Sum(c => (long?)c.InputTokens ?? 0L),
                Output = g.Sum(c => (long?)c.OutputTokens ?? 0L),
                Cached = g.Sum(c => (long?)c.CachedInputTokens ?? 0L),
                LatencySum = g.Sum(c => c.LatencyMs ?? 0d),
                LatencyCount = g.Count(c => c.LatencyMs != null),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new StatisticsSummary(
            TotalCalls: agg?.Count ?? 0L,
            TotalInputTokens: agg?.Input ?? 0L,
            TotalOutputTokens: agg?.Output ?? 0L,
            TotalCachedInputTokens: agg?.Cached ?? 0L,
            AvgLatencyMs: agg is { LatencyCount: > 0 } ? agg.LatencySum / agg.LatencyCount : 0d,
            OverallPassRate: null);
    }

    /// <summary>
    /// Gets the earliest call asynchronously.
    /// </summary>
    public async Task<DateTimeOffset?> GetEarliestCallAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        return await Query(context, filter)
            .OrderBy(c => c.CreatedAt)
            .Select(c => (DateTimeOffset?)c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the token usage asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<TokenUsageStat>> GetTokenUsageAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        double widthMs = bucket.WidthMilliseconds();

        // Aggregate per (bucket, endpoint) in the database: only one row per non-empty bucket
        // crosses the wire — O(buckets × endpoints), never O(rows). See StatisticsTime.WidthMilliseconds.
        var rows = await q
            .GroupBy(c => new
            {
                Bucket = (int)Math.Floor((c.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                c.EndpointId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.EndpointId,
                Input = g.Sum(c => (long?)c.InputTokens ?? 0L),
                Output = g.Sum(c => (long?)c.OutputTokens ?? 0L),
                Cached = g.Sum(c => (long?)c.CachedInputTokens ?? 0L),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new TokenUsageStat(
                BucketStart: bucket.BucketStartFromIndex(r.Bucket),
                EndpointId: r.EndpointId,
                InputTokens: r.Input,
                OutputTokens: r.Output,
                CachedInputTokens: r.Cached))
            .OrderBy(s => s.BucketStart)
            .ThenBy(s => s.EndpointId)
            .ToArray();
    }

    /// <summary>
    /// Gets the latency asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<LatencyStat>> GetLatencyAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();

        // Percentiles need the full ordered distribution. On a relational provider we compute them
        // server-side with percentile_cont so only one row per endpoint crosses the wire; pulling
        // every latency into memory and sorting there does not scale to millions of calls. The
        // in-memory provider (kiosk/tests) cannot translate ordered-set aggregates, so it keeps the
        // materialise-and-sort path — its datasets are small. Mirrors RemoveOlderThanAsync.
        if (context.Database.IsRelational())
        {
            return await GetLatencyRelationalAsync(context, filter, cancellationToken);
        }

        IQueryable<AgentCallEntity> q = Query(context, filter);
        var samples = await q
            .Where(c => c.LatencyMs != null)
            .Select(c => new { c.EndpointId, Latency = c.LatencyMs ?? 0 })
            .ToListAsync(cancellationToken);

        return samples
            .GroupBy(s => s.EndpointId)
            .Select(g =>
            {
                double[] sorted = g.Select(s => s.Latency).OrderBy(v => v).ToArray();
                return new LatencyStat(
                    EndpointId: g.Key,
                    P50Ms: Percentile(sorted, 0.50),
                    P95Ms: Percentile(sorted, 0.95),
                    P99Ms: Percentile(sorted, 0.99),
                    MinMs: sorted[0],
                    MaxMs: sorted[^1],
                    SampleCount: sorted.Length);
            })
            .OrderBy(s => s.EndpointId)
            .ToArray();
    }

    private async Task<IReadOnlyList<LatencyStat>> GetLatencyRelationalAsync(
        StorageDbContext context, StatisticsFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildLatencyWhere(context, filter);
        // One ordered-set aggregate over an ARRAY of probabilities: PostgreSQL computes p50/p95/p99
        // from a single ordered pass and returns a double[] (vs. three separate percentile_cont calls).
        string sql = $"""
            SELECT "EndpointId",
                   count(*) AS cnt,
                   min("LatencyMs") AS mn,
                   max("LatencyMs") AS mx,
                   percentile_cont(ARRAY[0.5, 0.95, 0.99]) WITHIN GROUP (ORDER BY "LatencyMs") AS ps
            FROM "AgentCallEntity"
            WHERE "LatencyMs" IS NOT NULL{where}
            GROUP BY "EndpointId"
            ORDER BY "EndpointId"
            """;

        var result = new List<LatencyStat>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                double[] percentiles = reader.GetFieldValue<double[]>(4);
                result.Add(new LatencyStat(
                    EndpointId: reader.GetGuid(0),
                    P50Ms: percentiles[0],
                    P95Ms: percentiles[1],
                    P99Ms: percentiles[2],
                    MinMs: reader.GetDouble(2),
                    MaxMs: reader.GetDouble(3),
                    SampleCount: (int)reader.GetInt64(1)));
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
        return result;
    }

    /// <summary>
    /// Builds the parameterised <c>WHERE</c> fragment (and its parameters) mirroring
    /// <see cref="Query"/> for the raw-SQL percentile paths. Kept in lockstep with the table/column
    /// names in <c>AgentCallConfig</c>/<c>AgentVersionConfig</c>/<c>AgentConfig</c>.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than private so <c>StatisticsFilterWhereTests</c> can assert the
    /// generated fragment directly: the behavioural tests run on the in-memory provider, where the
    /// percentile paths fall back to LINQ, so this SQL is otherwise only exercised in production.
    /// </remarks>
    internal static (string Where, IReadOnlyList<(string Name, object Value)> Parameters) BuildLatencyWhere(
        StorageDbContext context, StatisticsFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();

        if (filter.AgentId is { } agentId)
        {
            clauses.Add("\"AgentVersionId\" IN (SELECT \"Id\" FROM \"AgentVersionEntity\" WHERE \"AgentId\" = @agentId)");
            parameters.Add(("@agentId", agentId));
        }
        if (filter.ProjectId is { } projectId)
        {
            clauses.Add("\"AgentVersionId\" IN (SELECT \"Id\" FROM \"AgentVersionEntity\" WHERE \"Project\" = @projectId)");
            parameters.Add(("@projectId", projectId));
        }
        if (filter.ProjectIds is { Count: > 0 } projectIds)
        {
            // Multi-project scope (#483). The ids go over as ONE uuid[] parameter compared with
            // = ANY — never interpolated into the statement, and never a variable-length IN list,
            // so the SQL text is constant regardless of how many projects the caller belongs to
            // (Npgsql maps Guid[] to uuid[]). Separate from the single-project clause above so a
            // one-project scope keeps its equality predicate rather than a single-element array.
            clauses.Add("\"AgentVersionId\" IN (SELECT \"Id\" FROM \"AgentVersionEntity\" WHERE \"Project\" = ANY(@projectIds))");
            parameters.Add(("@projectIds", projectIds.ToArray()));
        }
        if (filter.EndpointId is { } endpointId)
        {
            clauses.Add("\"EndpointId\" = @endpointId");
            parameters.Add(("@endpointId", endpointId));
        }
        if (filter.From is { } from)
        {
            clauses.Add("\"CreatedAt\" >= @from");
            parameters.Add(("@from", from));
        }
        if (filter.To is { } to)
        {
            clauses.Add("\"CreatedAt\" <= @to");
            parameters.Add(("@to", to));
        }
        if (filter.ExcludeSystemAgents)
        {
            // Mirror Query(): drop calls whose AgentVersion belongs to a system agent (Tracey,
            // evaluators) so the raw-SQL percentile paths honour ExcludeSystemAgents like the LINQ
            // chokepoint. No parameter — the subquery is fully self-contained.
            clauses.Add("\"AgentVersionId\" NOT IN (SELECT v.\"Id\" FROM \"AgentVersionEntity\" v JOIN \"AgentEntity\" a ON v.\"AgentId\" = a.\"Id\" WHERE a.\"IsSystemAgent\")");
        }

        string where = clauses.Count == 0 ? string.Empty : " AND " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    /// <summary>
    /// Gets the error rates asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ErrorRateStat>> GetErrorRatesAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        var rows = await q
            .GroupBy(c => c.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Total = g.Count(),
                Errors = g.Count(c => c.HttpStatus >= 400),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ErrorRateStat(
                EndpointId: r.EndpointId,
                TotalCalls: r.Total,
                ErrorCalls: r.Errors,
                ErrorRate: r.Total == 0 ? 0d : r.Errors / (double)r.Total))
            .OrderBy(s => s.EndpointId)
            .ToArray();
    }

    /// <summary>
    /// Gets the model breakdown asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ModelBreakdownStat>> GetModelBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        // Same null-aware latency average as GetSummaryAsync: only calls with a latency contribute.
        var rows = await q
            .GroupBy(c => c.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                CallCount = g.Count(),
                TotalInput = g.Sum(c => (long?)c.InputTokens ?? 0L),
                TotalOutput = g.Sum(c => (long?)c.OutputTokens ?? 0L),
                TotalCached = g.Sum(c => (long?)c.CachedInputTokens ?? 0L),
                LatencySum = g.Sum(c => c.LatencyMs ?? 0d),
                LatencyCount = g.Count(c => c.LatencyMs != null),
            })
            .ToListAsync(cancellationToken);

        Guid[] endpointIds = rows.Select(r => r.EndpointId).ToArray();
        Dictionary<Guid, string> modelNames = await LoadModelNamesAsync(context, endpointIds, cancellationToken);

        return rows
            .Select(r => new ModelBreakdownStat(
                EndpointId: r.EndpointId,
                ModelName: modelNames.GetValueOrDefault(r.EndpointId, "(unknown)"),
                CallCount: r.CallCount,
                TotalInputTokens: r.TotalInput,
                TotalOutputTokens: r.TotalOutput,
                TotalCachedInputTokens: r.TotalCached,
                AvgDurationMs: r.LatencyCount > 0 ? r.LatencySum / r.LatencyCount : 0d))
            .OrderBy(s => s.ModelName)
            .ToArray();
    }

    /// <summary>
    /// Gets the agent breakdown asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<AgentBreakdownStat>> GetAgentBreakdownAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        var rows = await q
            .Join(context.Set<AgentVersionEntity>().AsNoTracking(),
                c => c.AgentVersionId, v => v.Id,
                (c, v) => new { v.AgentId })
            .GroupBy(x => x.AgentId)
            .Select(g => new AgentBreakdownStat(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

        return rows.OrderByDescending(r => r.CallCount).ThenBy(r => r.AgentId).ToArray();
    }

    /// <summary>
    /// Gets the cost estimate asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<CostEstimateStat>> GetCostEstimateAsync(StatisticsFilter filter, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        var rows = await q
            .GroupBy(c => c.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                TotalInput = g.Sum(c => (long?)c.InputTokens ?? 0L),
                TotalOutput = g.Sum(c => (long?)c.OutputTokens ?? 0L),
                TotalCached = g.Sum(c => (long?)c.CachedInputTokens ?? 0L),
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, rows.Select(r => r.EndpointId).ToArray(), cancellationToken);

        return rows
            .Select(r =>
            {
                IModelEndpoint? endpoint = endpoints.GetValueOrDefault(r.EndpointId);
                if (endpoint?.InputTokenCost is null && endpoint?.OutputTokenCost is null)
                {
                    return new CostEstimateStat(r.EndpointId, null, null, null);
                }

                // Cached input is a subset of the input tokens, priced at the cheaper cached rate
                // (falling back to the input rate when no cached price is configured). Per-token
                // prices are EUR per 1M tokens, so divide by 1M to match ModelEndpoint.CalculateCost.
                long cached = Math.Min(r.TotalCached, r.TotalInput);
                decimal? input = endpoint.InputTokenCost is { } ic
                    ? (ic * (r.TotalInput - cached) + (endpoint.CachedInputTokenCost ?? ic) * cached) / 1_000_000m
                    : null;
                decimal? output = endpoint.OutputTokenCost is { } oc ? oc * r.TotalOutput / 1_000_000m : null;
                decimal? total = (input ?? 0m) + (output ?? 0m);
                return new CostEstimateStat(r.EndpointId, input, output, total);
            })
            .OrderBy(s => s.EndpointId)
            .ToArray();
    }

    /// <summary>
    /// Gets the cost by project and agent asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ProjectAgentCostStat>> GetCostByProjectAndAgentAsync(
        StatisticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        // An AgentCall carries no project, only an AgentVersionId — the project (and the agent) hang
        // off AgentVersion, so the grouping keys come from a join. Grouping by endpoint as well keeps
        // pricing derived: token sums per model are enough to cost the group in C#, so the wire
        // carries O(projects × agents × endpoints) rows rather than O(calls).
        var rows = await q
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
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, rows.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken);

        return rows
            .GroupBy(r => new { r.Project, r.AgentId })
            .Select(g => new ProjectAgentCostStat(
                ProjectId: g.Key.Project,
                AgentId: g.Key.AgentId,
                CostEur: g.Sum(r => CostOf(endpoints, r.EndpointId, r.Input, r.Output, r.Cached))))
            .OrderBy(s => s.ProjectId)
            .ThenBy(s => s.AgentId)
            .ToArray();
    }

    /// <summary>
    /// Gets the cost series by agent asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<AgentCostPoint>> GetCostSeriesByAgentAsync(
        StatisticsFilter filter,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        double widthMs = bucket.WidthMilliseconds();

        var rows = await q
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
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, rows.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken);

        return rows
            .GroupBy(r => new { r.Bucket, r.AgentId })
            .Select(g => new AgentCostPoint(
                BucketStart: bucket.BucketStartFromIndex(g.Key.Bucket),
                AgentId: g.Key.AgentId,
                CostEur: g.Sum(r => CostOf(endpoints, r.EndpointId, r.Input, r.Output, r.Cached))))
            .OrderBy(p => p.BucketStart)
            .ThenBy(p => p.AgentId)
            .ToArray();
    }

    /// <summary>
    /// Gets the cost by api key asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ProjectApiKeyCostStat>> GetCostByApiKeyAsync(
        StatisticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        // ApiKeyId lives on the call itself, but the project still comes from the joined AgentVersion
        // (an AgentCall carries no project). Grouping by endpoint as well keeps pricing derived, so
        // the wire carries O(projects × keys × endpoints) rows rather than O(calls). Calls with no
        // key group under a null key — the unattributed remainder, kept rather than filtered out so
        // the per-key figures still sum to the project total.
        var rows = await q
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
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, rows.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken);

        return rows
            .GroupBy(r => new { r.Project, r.ApiKeyId })
            .Select(g => new ProjectApiKeyCostStat(
                ProjectId: g.Key.Project,
                ApiKeyId: g.Key.ApiKeyId,
                CostEur: g.Sum(r => CostOf(endpoints, r.EndpointId, r.Input, r.Output, r.Cached))))
            .OrderBy(s => s.ProjectId)
            .ThenBy(s => s.ApiKeyId)
            .ToArray();
    }

    /// <summary>
    /// Gets the cost series by api key asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<ApiKeyCostPoint>> GetCostSeriesByApiKeyAsync(
        StatisticsFilter filter,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        double widthMs = bucket.WidthMilliseconds();

        // No AgentVersion join here: unlike the (project, agent) series, both grouping keys —
        // the bucket and the key id — are columns of the call itself, and the window is already
        // project-scoped by the filter.
        var rows = await q
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
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, rows.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken);

        return rows
            .GroupBy(r => new { r.Bucket, r.ApiKeyId })
            .Select(g => new ApiKeyCostPoint(
                BucketStart: bucket.BucketStartFromIndex(g.Key.Bucket),
                ApiKeyId: g.Key.ApiKeyId,
                CostEur: g.Sum(r => CostOf(endpoints, r.EndpointId, r.Input, r.Output, r.Cached))))
            .OrderBy(p => p.BucketStart)
            .ThenBy(p => p.ApiKeyId)
            .ToArray();
    }

    /// <summary>
    /// Determines whether the unpriced endpoints asynchronously.
    /// </summary>
    public async Task<bool> HasUnpricedEndpointsAsync(
        StatisticsFilter filter,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();

        // Only the distinct endpoint ids of the window cross the wire (one row per endpoint), then a
        // single indexed lookup decides pricing — no per-call work either side.
        List<Guid> endpointIds = await Query(context, filter)
            .Select(c => c.EndpointId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (endpointIds.Count == 0)
            return false;

        return await context.Set<ModelEndpointEntity>()
            .AsNoTracking()
            .AnyAsync(
                e => endpointIds.Contains(e.Id) && e.InputTokenCost == null && e.OutputTokenCost == null,
                cancellationToken);
    }

    /// <summary>
    /// Prices one token group from its endpoint. An endpoint with no configured price contributes
    /// zero (the same choice GetCostEstimateAsync/GetAgentWindowAsync make) — the undercount is
    /// surfaced separately via <see cref="HasUnpricedEndpointsAsync"/> rather than guessed at.
    /// </summary>
    private static decimal CostOf(
        Dictionary<Guid, IModelEndpoint> endpoints, Guid endpointId, long input, long output, long cached)
        => endpoints.TryGetValue(endpointId, out IModelEndpoint? e)
            ? e.CalculateCost(new TokenUsage(
                (ulong)Math.Max(input, 0L), (ulong)Math.Max(output, 0L), (ulong)Math.Max(cached, 0L))) ?? 0m
            : 0m;

    /// <summary>
    /// Gets the token usage by agent asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<AgentTokenUsageStat>> GetTokenUsageByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter);

        double widthMs = bucket.WidthMilliseconds();

        var rows = await q
            .Join(context.Set<AgentVersionEntity>().AsNoTracking(),
                c => c.AgentVersionId, v => v.Id,
                (c, v) => new { c.CreatedAt, v.AgentId, c.InputTokens, c.OutputTokens, c.CachedInputTokens })
            .GroupBy(x => new
            {
                Bucket = (int)Math.Floor((x.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                x.AgentId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.AgentId,
                Input = g.Sum(x => (long?)x.InputTokens ?? 0L),
                Output = g.Sum(x => (long?)x.OutputTokens ?? 0L),
                Cached = g.Sum(x => (long?)x.CachedInputTokens ?? 0L),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AgentTokenUsageStat(
                BucketStart: bucket.BucketStartFromIndex(r.Bucket),
                AgentId: r.AgentId,
                InputTokens: r.Input,
                OutputTokens: r.Output,
                CachedInputTokens: r.Cached))
            .OrderBy(s => s.BucketStart)
            .ThenBy(s => s.AgentId)
            .ToArray();
    }

    /// <summary>
    /// Every statistical (ingestion-time) outlier bit — the whole mask except the async
    /// <see cref="OutlierFlags.CustomAnomaly"/> bit. Kept in lockstep with <see cref="OutlierFlags"/>.
    /// </summary>
    private const OutlierFlags StaticOutlierBits =
        OutlierFlags.HighTokens | OutlierFlags.HighLatency | OutlierFlags.LowCacheHit | OutlierFlags.ManyToolCalls;

    /// <summary>
    /// Gets the anomaly counts by agent asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<AgentAnomalyStat>> GetAnomalyCountsByAgentAsync(StatisticsFilter filter, StatisticsBucket bucket, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();

        // The non-zero-flags predicate matches the partial outlier index (WHERE "OutlierFlags" <> 0,
        // see AgentCallConfig), so only the small flagged fraction of the table is ever scanned. The
        // integer-slot GROUP BY and the conditional bitmask counts all translate server-side — the
        // same shape as GetTokenUsageByAgentAsync, with Count(predicate) like GetErrorRatesAsync.
        // Keep the shape in sync with StatsQueryTranslationTests.AnomalyCountsAggregate_….
        double widthMs = bucket.WidthMilliseconds();

        var rows = await Query(context, filter)
            .Where(c => c.OutlierFlags != OutlierFlags.None)
            .Join(context.Set<AgentVersionEntity>().AsNoTracking(),
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
                Static = g.Count(x => (x.OutlierFlags & StaticOutlierBits) != OutlierFlags.None),
                Custom = g.Count(x => (x.OutlierFlags & OutlierFlags.CustomAnomaly) != OutlierFlags.None),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AgentAnomalyStat(
                BucketStart: bucket.BucketStartFromIndex(r.Bucket),
                AgentId: r.AgentId,
                StaticCount: r.Static,
                CustomCount: r.Custom))
            .OrderBy(s => s.BucketStart)
            .ThenBy(s => s.AgentId)
            .ToArray();
    }

    /// <summary>
    /// Gets the live telemetry asynchronously.
    /// </summary>
    public async Task<LiveTelemetry> GetLiveTelemetryAsync(StatisticsFilter filter, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        // Live telemetry is always the [since, now] window. Build the query from a filter whose
        // From/To are pinned to that window so the scalar rollups and the percentile path below see
        // exactly the same rows (rather than also re-applying the caller's own From/To).
        StatisticsFilter windowFilter = filter with { From = since, To = now };
        IQueryable<AgentCallEntity> q = Query(context, windowFilter);

        // Scalar rollups (count / error count / token sum) aggregate server-side in a single row.
        var agg = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Errors = g.Count(c => c.HttpStatus >= 400),
                Tokens = g.Sum(c => ((long?)c.InputTokens ?? 0L) + ((long?)c.OutputTokens ?? 0L)),
            })
            .FirstOrDefaultAsync(cancellationToken);

        double windowMinutes = Math.Max((now - since).TotalMinutes, 1d);
        double windowSeconds = Math.Max((now - since).TotalSeconds, 1d);
        int total = agg?.Total ?? 0;
        int errors = agg?.Errors ?? 0;
        long tokens = agg?.Tokens ?? 0L;

        double p95;
        if (context.Database.IsRelational())
        {
            p95 = await GetWindowPercentileRelationalAsync(context, windowFilter, 0.95, cancellationToken);
        }
        else
        {
            double[] sorted = await q
                .Where(c => c.LatencyMs != null)
                .Select(c => c.LatencyMs ?? 0d)
                .OrderBy(v => v)
                .ToArrayAsync(cancellationToken);
            p95 = Percentile(sorted, 0.95);
        }

        return new LiveTelemetry(
            TracesPerMinute: total / windowMinutes,
            TokensPerSecond: tokens / windowSeconds,
            QueueDepth: 0,
            ErrorRate: total == 0 ? 0d : errors / (double)total,
            P95Ms: p95);
    }

    private async Task<double> GetWindowPercentileRelationalAsync(
        StorageDbContext context, StatisticsFilter filter, double percentile, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildLatencyWhere(context, filter);
        string sql = $"""
            SELECT percentile_cont({percentile.ToString(System.Globalization.CultureInfo.InvariantCulture)})
                   WITHIN GROUP (ORDER BY "LatencyMs")
            FROM "AgentCallEntity"
            WHERE "LatencyMs" IS NOT NULL{where}
            """;

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            command.Parameters.Add(p);
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            object? scalar = await command.ExecuteScalarAsync(cancellationToken);
            return scalar is double d ? d : 0d;
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Gets the call trends asynchronously.
    /// </summary>
    public async Task<CallTrends> GetCallTrendsAsync(StatisticsFilter filter, int buckets, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        buckets = Math.Max(buckets, 1);
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter)
            .Where(c => c.CreatedAt >= from && c.CreatedAt <= to);

        double bucketMs = Math.Max((to - from).TotalMilliseconds, 1d) / buckets;
        double bucketSeconds = bucketMs / 1000d;

        // Bucket-and-aggregate in the database (same integer-slot GROUP BY as the histogram), so at
        // most one row per non-empty bucket returns instead of every matching call.
        var grouped = await q
            .GroupBy(c => (int)Math.Floor((c.CreatedAt - from).TotalMilliseconds / bucketMs))
            .Select(g => new
            {
                Index = g.Key,
                Count = g.Count(),
                LatencySum = g.Sum(c => c.LatencyMs ?? 0d),
                LatencyCount = g.Count(c => c.LatencyMs != null),
                TokenSum = g.Sum(c => ((long?)c.InputTokens ?? 0L) + ((long?)c.OutputTokens ?? 0L)),
            })
            .ToListAsync(cancellationToken);

        var traces = new double[buckets];
        var latencySum = new double[buckets];
        var latencyCount = new int[buckets];
        var tokenSum = new double[buckets];

        foreach (var g in grouped)
        {
            int idx = Math.Clamp(g.Index, 0, buckets - 1);
            traces[idx] += g.Count;
            tokenSum[idx] += g.TokenSum;
            latencySum[idx] += g.LatencySum;
            latencyCount[idx] += g.LatencyCount;
        }

        var latencyMs = new double[buckets];
        var throughput = new double[buckets];
        for (int i = 0; i < buckets; i++)
        {
            latencyMs[i] = latencyCount[i] > 0 ? latencySum[i] / latencyCount[i] : 0d;
            throughput[i] = tokenSum[i] / bucketSeconds;
        }

        return new CallTrends(traces, latencyMs, throughput);
    }

    /// <summary>
    /// Gets the pulse asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<int>> GetPulseAsync(StatisticsFilter filter, DateTimeOffset from, DateTimeOffset to, int buckets, CancellationToken cancellationToken = default)
    {
        buckets = Math.Max(buckets, 1);
        StorageDbContext context = contextFactory();
        IQueryable<AgentCallEntity> q = Query(context, filter)
            .Where(c => c.CreatedAt >= from && c.CreatedAt <= to);

        double bucketMs = Math.Max((to - from).TotalMilliseconds, 1d) / buckets;

        // Same integer-slot GROUP BY as GetCallTrendsAsync: at most one row per non-empty bucket
        // returns instead of every matching call. Keep the shape in sync with
        // StatsQueryTranslationTests.PulseAggregate_PerMinuteCountBuckets_TranslatesToServerSideGroupBy.
        var grouped = await q
            .GroupBy(c => (int)Math.Floor((c.CreatedAt - from).TotalMilliseconds / bucketMs))
            .Select(g => new { Index = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var counts = new int[buckets];
        foreach (var g in grouped)
        {
            counts[Math.Clamp(g.Index, 0, buckets - 1)] += g.Count;
        }

        return counts;
    }

    /// <summary>
    /// Gets the agent time series asynchronously.
    /// </summary>
    public async Task<IReadOnlyList<AgentTimeSeriesPoint>> GetAgentTimeSeriesAsync(
        Guid agentId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<AgentTimeSeriesPoint> series, _) = await GetAgentWindowAsync(agentId, from, to, bucket, cancellationToken);
        return series;
    }

    /// <summary>
    /// Gets the agent window asynchronously.
    /// </summary>
    public async Task<(IReadOnlyList<AgentTimeSeriesPoint> Series, AgentTimeSummary Summary)> GetAgentWindowAsync(
        Guid agentId,
        DateTimeOffset from,
        DateTimeOffset to,
        StatisticsBucket bucket,
        CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();
        double widthMs = bucket.WidthMilliseconds();

        var versionIdsForAgent = context.Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .Select(v => v.Id);

        // Aggregate per (bucket, endpoint) in the database. The endpoint split lets us cost each
        // bucket from per-model token sums (CalculateCost is linear in tokens) without ever pulling
        // raw rows into memory — the result set is O(buckets × endpoints), not O(calls).
        var grouped = await context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(c => versionIdsForAgent.Contains(c.AgentVersionId) && c.CreatedAt >= from && c.CreatedAt <= to)
            .GroupBy(c => new
            {
                Bucket = (int)Math.Floor((c.CreatedAt - DateTimeOffset.UnixEpoch).TotalMilliseconds / widthMs),
                c.EndpointId,
            })
            .Select(g => new
            {
                g.Key.Bucket,
                g.Key.EndpointId,
                Count = g.Count(),
                Input = g.Sum(c => (long?)c.InputTokens ?? 0L),
                Output = g.Sum(c => (long?)c.OutputTokens ?? 0L),
                Cached = g.Sum(c => (long?)c.CachedInputTokens ?? 0L),
                LatencySum = g.Sum(c => c.LatencyMs ?? 0d),
            })
            .ToListAsync(cancellationToken);

        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, grouped.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken);

        decimal CostOf(Guid endpointId, long input, long output, long cached)
            => endpoints.TryGetValue(endpointId, out IModelEndpoint? e)
                ? e.CalculateCost(new TokenUsage(
                    (ulong)Math.Max(input, 0L), (ulong)Math.Max(output, 0L), (ulong)Math.Max(cached, 0L))) ?? 0m
                : 0m;

        AgentTimeSeriesPoint[] series = grouped
            .GroupBy(r => r.Bucket)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                int count = g.Sum(r => r.Count);
                return new AgentTimeSeriesPoint(
                    BucketStart: bucket.BucketStartFromIndex(g.Key),
                    TraceCount: count,
                    InputTokens: g.Sum(r => r.Input),
                    OutputTokens: g.Sum(r => r.Output),
                    CachedInputTokens: g.Sum(r => r.Cached),
                    CostEur: g.Sum(r => CostOf(r.EndpointId, r.Input, r.Output, r.Cached)),
                    AvgLatencyMs: count == 0 ? 0d : g.Sum(r => r.LatencySum) / count);
            })
            .ToArray();

        int totalTraces = grouped.Sum(r => r.Count);
        AgentTimeSummary summary = new(
            TotalTraces: totalTraces,
            TotalInputTokens: grouped.Sum(r => r.Input),
            TotalOutputTokens: grouped.Sum(r => r.Output),
            TotalCachedInputTokens: grouped.Sum(r => r.Cached),
            TotalCostEur: grouped.Sum(r => CostOf(r.EndpointId, r.Input, r.Output, r.Cached)),
            AvgLatencyMs: totalTraces == 0 ? 0d : grouped.Sum(r => r.LatencySum) / totalTraces);

        return (series, summary);
    }

    /// <summary>
    /// Gets the agent distributions asynchronously.
    /// </summary>
    public async Task<AgentCallDistributions> GetAgentDistributionsAsync(
        Guid agentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        StorageDbContext context = contextFactory();

        IQueryable<Guid> versionIdsForAgent = context.Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .Select(v => v.Id);

        // Single materialise-and-compute pass. Std-dev needs no ordered-set aggregate (unlike the
        // latency percentiles above), and cost-per-conversation needs C# endpoint pricing, so every
        // distribution is computed here rather than splitting relational/in-memory paths.
        //
        // The window is caller-supplied and unbounded, so the row set is capped at
        // MaxDistributionSamples most-recent calls: a request spanning a busy agent's whole history
        // would otherwise materialize its entire trace history at once.
        var calls = await context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(c => versionIdsForAgent.Contains(c.AgentVersionId)
                && c.CreatedAt >= from && c.CreatedAt <= to
                && c.HttpStatus >= 200 && c.HttpStatus < 300)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Select(c => new
            {
                ConvKey = c.ConversationId ?? c.Id,
                c.EndpointId,
                c.CreatedAt,
                c.InputTokens,
                c.OutputTokens,
                c.CachedInputTokens,
                c.LatencyMs,
                Tools = c.ResponseToolRequestCount,
            })
            .Take(MaxDistributionSamples)
            .ToListAsync(cancellationToken);

        if (calls.Count == 0)
        {
            return AgentCallDistributions.Empty;
        }

        if (calls.Count == MaxDistributionSamples)
        {
            // Never truncate silently — the caller is looking at a sample, not the whole window.
            logger.LogInformation(
                "Distributions for agent {AgentId} over {From:o}–{To:o} hit the {Cap}-call sample cap; "
                + "the returned distributions describe the most recent {Cap} calls in that window.",
                agentId, from, to, MaxDistributionSamples, MaxDistributionSamples);
        }

        // Per-call metrics.
        MetricDistribution inputTokens = Distribution(calls.Select(c => (double)(c.InputTokens ?? 0UL)).ToArray());
        MetricDistribution outputTokens = Distribution(calls.Select(c => (double)(c.OutputTokens ?? 0UL)).ToArray());
        MetricDistribution latency = Distribution(
            calls.Where(c => c.LatencyMs != null).Select(c => c.LatencyMs ?? 0d).ToArray());

        // Per-conversation metrics. Cost is linear in tokens, so a conversation's cost is the sum over
        // its calls of CostOf(endpoint, tokens); load each endpoint's pricing once. Unpriced endpoints
        // contribute 0 (matches GetCostEstimateAsync/GetAgentWindowAsync).
        Dictionary<Guid, IModelEndpoint> endpoints = await LoadEndpointsAsync(
            context, calls.Select(c => c.EndpointId).Distinct().ToArray(), cancellationToken);

        decimal CostOf(Guid endpointId, long input, long output, long cached)
            => endpoints.TryGetValue(endpointId, out IModelEndpoint? e)
                ? e.CalculateCost(new TokenUsage(
                    (ulong)Math.Max(input, 0L), (ulong)Math.Max(output, 0L), (ulong)Math.Max(cached, 0L))) ?? 0m
                : 0m;

        var costSamples = new List<double>();
        var toolSamples = new List<double>();
        var cacheSamples = new List<double>();

        foreach (var conv in calls.GroupBy(c => c.ConvKey))
        {
            decimal cost = conv.Sum(c => CostOf(
                c.EndpointId, (long)(c.InputTokens ?? 0UL), (long)(c.OutputTokens ?? 0UL), (long)(c.CachedInputTokens ?? 0UL)));
            costSamples.Add((double)cost);

            toolSamples.Add(conv.Sum(c => (double)c.Tools));

            // Cache hit rate for turn ≥ 2: drop the earliest call; sample only when later turns sent
            // input (turn 1 cannot be a cache hit; single-turn conversations contribute nothing).
            var laterTurns = conv.OrderBy(c => c.CreatedAt).Skip(1).ToArray();
            if (laterTurns.Length > 0)
            {
                long laterInput = laterTurns.Sum(c => (long)(c.InputTokens ?? 0UL));
                if (laterInput > 0L)
                {
                    long laterCached = laterTurns.Sum(c => (long)(c.CachedInputTokens ?? 0UL));
                    cacheSamples.Add(Math.Clamp(laterCached / (double)laterInput, 0d, 1d));
                }
            }
        }

        return new AgentCallDistributions(
            InputTokensPerCall: inputTokens,
            OutputTokensPerCall: outputTokens,
            LatencyMsPerCall: latency,
            CostPerConversationEur: Distribution(costSamples),
            CacheHitRatePerConversation: Distribution(cacheSamples),
            ToolCallsPerConversation: Distribution(toolSamples));
    }

    private async Task<Dictionary<Guid, IModelEndpoint>> LoadEndpointsAsync(
        StorageDbContext context, IReadOnlyCollection<Guid> endpointIds, CancellationToken cancellationToken)
    {
        if (endpointIds.Count == 0)
        {
            return new Dictionary<Guid, IModelEndpoint>();
        }

        List<ModelEndpointEntity> entities = await context.Set<ModelEndpointEntity>()
            .AsNoTracking()
            .Where(e => endpointIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, IModelEndpoint>(entities.Count);
        foreach (ModelEndpointEntity entity in entities)
        {
            result[entity.Id] = await endpointMapper.Map(entity, cancellationToken);
        }
        return result;
    }

    private static async Task<Dictionary<Guid, string>> LoadModelNamesAsync(
        StorageDbContext context, IReadOnlyCollection<Guid> endpointIds, CancellationToken cancellationToken)
    {
        if (endpointIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var pairs = await context.Set<ModelEndpointEntity>()
            .AsNoTracking()
            .Where(e => endpointIds.Contains(e.Id))
            .Join(context.Set<ModelEntity>().AsNoTracking(),
                e => e.Model,
                m => m.Id,
                (e, m) => new { EndpointId = e.Id, m.Name })
            .ToListAsync(cancellationToken);

        return pairs.ToDictionary(p => p.EndpointId, p => p.Name);
    }

    private static IQueryable<AgentCallEntity> Query(StorageDbContext context, StatisticsFilter filter)
    {
        IQueryable<AgentCallEntity> query = context.Set<AgentCallEntity>().AsNoTracking();

        if (filter.AgentId is { } agentId)
        {
            IQueryable<Guid> versionIdsForAgent = context.Set<AgentVersionEntity>()
                .AsNoTracking()
                .Where(v => v.AgentId == agentId)
                .Select(v => v.Id);
            query = query.Where(c => versionIdsForAgent.Contains(c.AgentVersionId));
        }
        if (filter.EndpointId is { } endpointId)
        {
            query = query.Where(c => c.EndpointId == endpointId);
        }
        if (filter.ProjectId is { } projectId)
        {
            IQueryable<Guid> versionIdsForProject = context.Set<AgentVersionEntity>()
                .AsNoTracking()
                .Where(v => v.Project == projectId)
                .Select(v => v.Id);
            query = query.Where(c => versionIdsForProject.Contains(c.AgentVersionId));
        }
        // Multi-project scope (#483): an unfiltered aggregate from a caller who may read several
        // projects. Same shape as the single-project branch above — a semi-join against
        // AgentVersion(Project), with IN instead of = — so it stays server-side rather than
        // degenerating into a client-side filter over every trace row. Kept separate from that
        // branch so a scope naming exactly one project keeps its equality predicate and its plan.
        if (filter.ProjectIds is { Count: > 0 } projectIds)
        {
            IQueryable<Guid> versionIdsForProjects = context.Set<AgentVersionEntity>()
                .AsNoTracking()
                .Where(v => projectIds.Contains(v.Project))
                .Select(v => v.Id);
            query = query.Where(c => versionIdsForProjects.Contains(c.AgentVersionId));
        }
        if (filter.From is { } from)
        {
            query = query.Where(c => c.CreatedAt >= from);
        }
        if (filter.To is { } to)
        {
            query = query.Where(c => c.CreatedAt <= to);
        }
        if (filter.ExcludeSystemAgents)
        {
            // A call links to its agent via AgentVersion → Agent; drop the versions of system agents
            // so every aggregate built from this query (summary, model/agent breakdown, token series)
            // ignores the platform's own calls (Tracey, evaluators).
            IQueryable<Guid> systemVersionIds = context.Set<AgentVersionEntity>()
                .AsNoTracking()
                .Join(context.Set<AgentEntity>(), v => v.AgentId, a => a.Id, (v, a) => new { v.Id, a.IsSystemAgent })
                .Where(x => x.IsSystemAgent)
                .Select(x => x.Id);
            query = query.Where(c => !systemVersionIds.Contains(c.AgentVersionId));
        }
        return query;
    }

    /// <summary>
    /// Mean, sample (n−1) standard deviation, min/max and an equal-width histogram of
    /// <paramref name="samples"/>. Std-dev is 0 when fewer than two samples exist; an empty set yields
    /// <see cref="MetricDistribution.Empty"/>.
    /// </summary>
    private static MetricDistribution Distribution(IReadOnlyList<double> samples)
    {
        int n = samples.Count;
        if (n == 0)
        {
            return MetricDistribution.Empty;
        }

        double mean = samples.Sum() / n;
        double std = 0d;
        if (n >= 2)
        {
            double sumSq = 0d;
            foreach (double v in samples)
            {
                double d = v - mean;
                sumSq += d * d;
            }
            std = Math.Sqrt(sumSq / (n - 1));
        }

        double min = samples.Min();
        double max = samples.Max();
        return new MetricDistribution(mean, std, n, min, max, BuildHistogram(samples, min, max));
    }

    /// <summary>
    /// Bins <paramref name="samples"/> into up to 24 equal-width buckets over [min, max]. The bin count
    /// is capped at the number of distinct values, so small discrete metrics (e.g. tool counts) get one
    /// bar per value rather than fractional empties. When every sample is identical, a single bin holds
    /// them all. The max value lands in the last bin (the top edge is treated as inclusive).
    /// </summary>
    private static IReadOnlyList<HistogramBin> BuildHistogram(IReadOnlyList<double> samples, double min, double max)
    {
        if (max <= min)
        {
            return [new HistogramBin(min, max, samples.Count)];
        }

        int bins = Math.Clamp(samples.Distinct().Count(), 1, 24);
        double width = (max - min) / bins;
        var counts = new int[bins];
        foreach (double v in samples)
        {
            int idx = (int)((v - min) / width);
            counts[Math.Clamp(idx, 0, bins - 1)]++;
        }

        var result = new HistogramBin[bins];
        for (int i = 0; i < bins; i++)
        {
            result[i] = new HistogramBin(min + (i * width), min + ((i + 1) * width), counts[i]);
        }
        return result;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0d;
        }
        if (sorted.Length == 1)
        {
            return sorted[0];
        }
        double rank = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sorted[lower];
        }
        double weight = rank - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}
