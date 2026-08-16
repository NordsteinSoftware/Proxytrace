using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Session;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.ModelEndpoint;
using Nordstein.Core.Domain.Paging;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Search;
using Nordstein.Core.AI.Completions;
using Proxytrace.Storage.Internal.Entities.Agent;
using Proxytrace.Storage.Internal.Entities.AgentVersion;
using Proxytrace.Storage.Internal.Entities.Model;
using Proxytrace.Storage.Internal.Entities.ModelEndpoint;

namespace Proxytrace.Storage.Internal.Entities.AgentCall;

[UsedImplicitly]
internal class AgentCallRepository : AbstractRepository<IAgentCall, AgentCallEntity>, IAgentCallRepository
{
    private const int MaxFulltextHits = 1000;

    private readonly ISearchService searchService;
    private readonly IRepository<IAgentVersion> versions;
    private readonly IRepository<IAgent> agents;
    private readonly IRepository<IModelEndpoint> endpoints;

    /// <summary>
    /// Initializes the repository with the base infrastructure plus references to the version, agent,
    /// and endpoint repositories used to resolve metadata for list projections and query filters.
    /// </summary>
    public AgentCallRepository(
        IMapper<IAgentCall, AgentCallEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        ISearchService searchService,
        IRepository<IAgentVersion> versions,
        IRepository<IAgent> agents,
        IRepository<IModelEndpoint> endpoints,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.searchService = searchService;
        this.versions = versions;
        this.agents = agents;
        this.endpoints = endpoints;
    }

    /// <summary>
    /// Returns a paged set of fully-hydrated agent calls matching the given filter, together with the
    /// total count. Applies all filter predicates (project, agent, endpoint, date range, status, tokens,
    /// latency, outlier flags, tool name, fulltext) before paging.
    /// </summary>
    public async Task<(IReadOnlyList<IAgentCall> Items, int Total)> GetFilteredAsync(
        AgentCallFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = await BuildFilteredQueryAsync(context, filter, cancellationToken);
        if (query is null)
        {
            return ([], 0);
        }

        var total = await query.CountAsync(cancellationToken);

        var stored = await ApplySort(query, filter)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await Map(stored, cancellationToken);
        return (items, total);
    }

    /// <summary>
    /// Returns a paged list of lightweight trace summaries matching the given filter, together with the
    /// total count. Projects scalar columns only — the large Request/Response JSON payloads are never
    /// read. Agent and endpoint metadata is batch-resolved from the cached entity repositories.
    /// </summary>
    public async Task<(IReadOnlyList<AgentCallListItem> Items, int Total)> GetFilteredListAsync(
        AgentCallFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = await BuildFilteredQueryAsync(context, filter, cancellationToken);
        if (query is null)
        {
            return ([], 0);
        }

        var total = await query.CountAsync(cancellationToken);

        // Project scalar columns only — the Request/Response/ModelParameters payload columns are
        // never read, so a page does not materialise (or transfer) large conversation JSON.
        var rows = await ApplySort(query, filter)
            .Skip(Paging.Offset(page, pageSize))
            .Take(pageSize)
            .Select(e => new ListRow(
                e.Id,
                e.AgentVersionId,
                e.EndpointId,
                e.InputTokens,
                e.OutputTokens,
                e.CachedInputTokens,
                e.LatencyMs,
                e.HttpStatus,
                e.FinishReason,
                e.ErrorMessage,
                e.RequestPreview,
                e.ResponseToolRequestCount,
                e.CreatedAt,
                e.UpdatedAt,
                e.ConversationId,
                e.SessionId,
                e.OutlierFlags))
            .ToListAsync(cancellationToken);

        // Resolve the agent/endpoint metadata the list shows from the cached entity repositories
        // (batched by distinct id), rather than per-row navigation loads.
        var versionsById = (await versions.GetManyAsync(
                rows.Select(r => r.AgentVersionId).Distinct().ToArray(),
                ignoreMissing: true,
                cancellationToken: cancellationToken))
            .ToDictionary(v => v.Id);
        var agentsById = (await agents.GetManyAsync(
                versionsById.Values.Select(v => v.AgentId).Distinct().ToArray(),
                ignoreMissing: true,
                cancellationToken: cancellationToken))
            .ToDictionary(a => a.Id);
        var endpointsById = (await endpoints.GetManyAsync(
                rows.Select(r => r.EndpointId).Distinct().ToArray(),
                ignoreMissing: true,
                cancellationToken: cancellationToken))
            .ToDictionary(e => e.Id);

        var items = rows.Select(r =>
        {
            Guid agentId = versionsById.TryGetValue(r.AgentVersionId, out var version) ? version.AgentId : Guid.Empty;
            agentsById.TryGetValue(agentId, out var agent);
            endpointsById.TryGetValue(r.EndpointId, out var endpoint);

            decimal? cost = endpoint is not null && r.InputTokens.HasValue && r.OutputTokens.HasValue
                ? endpoint.CalculateCost(new TokenUsage(r.InputTokens.Value, r.OutputTokens.Value, r.CachedInputTokens ?? 0))
                : null;

            return new AgentCallListItem(
                Id: r.Id,
                AgentId: agentId,
                AgentName: agent?.Name ?? "(unknown)",
                ModelName: endpoint?.Model.Name ?? "(unknown)",
                ProviderName: endpoint?.Provider.Name ?? "(unknown)",
                MessagePreview: r.RequestPreview,
                ToolCount: r.ResponseToolRequestCount,
                InputTokens: r.InputTokens,
                OutputTokens: r.OutputTokens,
                CachedInputTokens: r.CachedInputTokens,
                LatencyMs: r.LatencyMs,
                HttpStatus: r.HttpStatus,
                FinishReason: r.FinishReason,
                ErrorMessage: r.ErrorMessage,
                Cost: cost,
                CreatedAt: r.CreatedAt,
                UpdatedAt: r.UpdatedAt,
                ConversationId: r.ConversationId,
                SessionId: r.SessionId,
                OutlierFlags: r.OutlierFlags);
        }).ToArray();

        return (items, total);
    }

    private sealed record ListRow(
        Guid Id,
        Guid AgentVersionId,
        Guid EndpointId,
        ulong? InputTokens,
        ulong? OutputTokens,
        ulong? CachedInputTokens,
        double? LatencyMs,
        int HttpStatus,
        string? FinishReason,
        string? ErrorMessage,
        string? RequestPreview,
        int ResponseToolRequestCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? ConversationId,
        Guid? SessionId,
        OutlierFlags OutlierFlags);

    /// <summary>
    /// Returns a time-bucketed histogram of call counts and error counts for the given filter window.
    /// Aggregates entirely in the database — one row per non-empty bucket — then expands to the
    /// requested bucket count with zero-filled gaps in the domain layer.
    /// </summary>
    public async Task<IReadOnlyList<AgentCallHistogramBucket>> GetHistogramAsync(
        AgentCallFilter filter,
        int buckets,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = await BuildFilteredQueryAsync(context, filter, cancellationToken);
        if (query is null)
        {
            return [];
        }

        var to = filter.To ?? DateTimeOffset.UtcNow;
        DateTimeOffset from;
        if (filter.From.HasValue)
        {
            from = filter.From.Value;
        }
        else
        {
            // One round-trip: a nullable Min is null exactly when nothing matches (index-backed).
            var earliest = await query
                .Select(e => (DateTimeOffset?)e.CreatedAt)
                .MinAsync(cancellationToken);
            if (earliest is null)
            {
                return [];
            }

            from = earliest.Value;
        }

        if (to <= from)
        {
            to = from.AddSeconds(1);
        }

        // Bucket and aggregate in the database: GROUP BY an integer slot index derived from each
        // row's offset into the window. The provider translates this to a single grouped aggregate
        // query, so only one row per non-empty bucket crosses the wire — O(buckets), not O(rows).
        // floor() (not a bare (int) cast) gives correct truncation: Npgsql renders a CAST-to-int as
        // a *rounding* CAST, which would misbucket boundary timestamps.
        var widthMs = (to - from).TotalMilliseconds / buckets;
        if (widthMs <= 0) widthMs = 1;

        var aggregated = await query
            .Where(e => e.CreatedAt >= from && e.CreatedAt <= to)
            .GroupBy(e => (int)Math.Floor((e.CreatedAt - from).TotalMilliseconds / widthMs))
            .Select(g => new
            {
                Index = g.Key,
                Total = g.Count(),
                Errors = g.Count(e => e.HttpStatus >= AgentCallHistogram.ErrorStatusThreshold),
            })
            .ToListAsync(cancellationToken);

        if (aggregated.Count == 0)
        {
            return [];
        }

        return AgentCallHistogram.Expand(
            aggregated.Select(a => (a.Index, a.Total, a.Errors)), from, to, buckets);
    }

    /// <summary>
    /// Returns aggregated statistics (total count, token usage, latency moments, error count, and cost)
    /// for all calls matching the given filter. Groups by endpoint so cost can be priced per endpoint;
    /// executes as a single grouped aggregate query.
    /// </summary>
    public async Task<AgentCallSummary> GetSummaryAsync(
        AgentCallFilter filter,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = await BuildFilteredQueryAsync(context, filter, cancellationToken);
        if (query is null)
        {
            return AgentCallSummary.Empty;
        }

        // GROUP BY endpoint because cost is priced per endpoint (CalculateCost), so it cannot be
        // summed in SQL. One row comes back per distinct endpoint in the result — bounded by how
        // many endpoints are configured, never by how many calls matched. Tokens, latency moments,
        // and the error count ride along in the same aggregate so this stays one round trip.
        //
        // The cached-token clamp is applied PER ROW rather than after aggregating: CalculateCost
        // clamps cached at input per call, so clamping post-aggregation would price differently
        // whenever any single row carried more cached tokens than input.
        //
        // Latency comes back as sum + sum-of-squares + count so the standard deviation can be
        // derived in the domain layer — EF cannot translate stddev_samp.
        var grouped = await query
            .GroupBy(e => e.EndpointId)
            .Select(g => new
            {
                EndpointId = g.Key,
                Count = g.Count(),
                InputTokens = g.Sum(e => (decimal?)e.InputTokens) ?? 0m,
                OutputTokens = g.Sum(e => (decimal?)e.OutputTokens) ?? 0m,
                CachedInputTokens = g.Sum(e => (decimal?)(
                    e.CachedInputTokens < e.InputTokens ? e.CachedInputTokens : e.InputTokens)) ?? 0m,
                LatencySum = g.Sum(e => e.LatencyMs) ?? 0d,
                LatencySumOfSquares = g.Sum(e => e.LatencyMs * e.LatencyMs) ?? 0d,
                LatencyCount = g.Count(e => e.LatencyMs != null),
                ErrorCount = g.Count(e => e.HttpStatus < 200 || e.HttpStatus >= 300),
            })
            .ToListAsync(cancellationToken);

        if (grouped.Count == 0)
        {
            return AgentCallSummary.Empty;
        }

        var endpointsById = (await endpoints.GetManyAsync(
                grouped.Select(g => g.EndpointId).Distinct().ToArray(),
                ignoreMissing: true,
                cancellationToken: cancellationToken))
            .ToDictionary(e => e.Id);

        var groups = grouped.Select(g => new AgentCallSummaryGroup(
            EndpointId: g.EndpointId,
            Count: g.Count,
            InputTokens: (ulong)g.InputTokens,
            OutputTokens: (ulong)g.OutputTokens,
            CachedInputTokens: (ulong)g.CachedInputTokens,
            LatencySum: g.LatencySum,
            LatencySumOfSquares: g.LatencySumOfSquares,
            LatencyCount: g.LatencyCount,
            ErrorCount: g.ErrorCount));

        return AgentCallSummary.Fold(
            groups,
            id => endpointsById.TryGetValue(id, out var endpoint) ? endpoint : null);
    }

    /// <summary>
    /// Builds the filtered (but unpaged, unordered) query shared by list + histogram reads.
    /// Returns <see langword="null"/> when the filter provably matches nothing (e.g. a fulltext
    /// query with no hits, or a fulltext query without a project scope).
    /// </summary>
    private async Task<IQueryable<AgentCallEntity>?> BuildFilteredQueryAsync(
        DbContext context,
        AgentCallFilter filter,
        CancellationToken cancellationToken)
    {
        var query = context.Set<AgentCallEntity>().AsNoTracking();

        if (filter.AgentId.HasValue)
        {
            var agentId = filter.AgentId.Value;
            var versionIdsForAgent = context.Set<AgentVersionEntity>()
                .Where(v => v.AgentId == agentId)
                .Select(v => v.Id);
            query = query.Where(e => versionIdsForAgent.Contains(e.AgentVersionId));
        }

        if (filter.ProjectId.HasValue)
        {
            var projectId = filter.ProjectId.Value;
            var versionIdsForProject = context.Set<AgentVersionEntity>()
                .Where(v => v.Project == projectId)
                .Select(v => v.Id);
            query = query.Where(e => versionIdsForProject.Contains(e.AgentVersionId));
        }

        // Multi-project scope (a non-admin listing without a project filter, #482). Same shape as
        // the single-project branch above — an IN over the agent-version subquery — so it stays a
        // server-side semi-join on AgentVersion(Project) rather than a client-side filter.
        if (filter.ProjectIds is { Count: > 0 } projectIds)
        {
            var versionIdsForProjects = context.Set<AgentVersionEntity>()
                .Where(v => projectIds.Contains(v.Project))
                .Select(v => v.Id);
            query = query.Where(e => versionIdsForProjects.Contains(e.AgentVersionId));
        }

        if (filter.EndpointId is not null)
        {
            query = query.Where(e => e.EndpointId == filter.EndpointId);
        }

        if (filter.ConversationId.HasValue)
        {
            query = query.Where(e => e.ConversationId == filter.ConversationId.Value);
        }

        if (filter.SessionId is { } sessionId)
        {
            query = query.Where(e => e.SessionId == sessionId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Model))
        {
            // Lower both sides and escape the user's wildcards — see LikePattern. A bare
            // EF.Functions.Like(m.Name, $"%{search}%") is case-sensitive on Postgres but
            // case-insensitive on the in-memory test provider, so the tests pass while the
            // production filter silently misses matches.
            var pattern = LikePattern.Contains(filter.Model);
            var matchingEndpointIds = context.Set<ModelEndpointEntity>()
                .Where(me => context.Set<ModelEntity>()
                    .Any(m => m.Id == me.Model
                              && EF.Functions.Like(m.Name.ToLower(), pattern, LikePattern.EscapeCharacter)))
                .Select(me => me.Id);
            query = query.Where(e => matchingEndpointIds.Contains(e.EndpointId));
        }

        if (filter.From.HasValue)
        {
            query = query.Where(e => e.CreatedAt >= filter.From.Value);
        }

        if (filter.To.HasValue)
        {
            query = query.Where(e => e.CreatedAt <= filter.To.Value);
        }

        if (filter.HttpStatus.HasValue)
        {
            query = query.Where(e => e.HttpStatus == filter.HttpStatus.Value);
        }

        if (filter.OutlierOnly)
        {
            query = query.Where(e => e.OutlierFlags != OutlierFlags.None);
        }

        if (filter.AnomalyFlags is { } anomalyFlags && anomalyFlags != OutlierFlags.None)
        {
            query = query.Where(e => (e.OutlierFlags & anomalyFlags) != 0);
        }

        if (filter.HttpStatusClass is { } statusClass)
        {
            var lower = statusClass * 100;
            query = query.Where(e => e.HttpStatus >= lower && e.HttpStatus < lower + 100);
        }

        if (filter.MinTokens.HasValue)
        {
            query = query.Where(e => e.TotalTokens >= filter.MinTokens.Value);
        }

        if (filter.MaxTokens.HasValue)
        {
            query = query.Where(e => e.TotalTokens <= filter.MaxTokens.Value);
        }

        if (filter.MinLatencyMs.HasValue)
        {
            query = query.Where(e => e.LatencyMs >= filter.MinLatencyMs.Value);
        }

        if (filter.MaxLatencyMs.HasValue)
        {
            query = query.Where(e => e.LatencyMs <= filter.MaxLatencyMs.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.ToolName))
        {
            var toolName = filter.ToolName;
            query = query.Where(e => context.Set<AgentCallToolEntity>()
                .Any(t => t.AgentCallId == e.Id && t.ToolName == toolName));
        }

        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            // The full-text index is partitioned per project, so a text query needs at least one
            // project to search. A multi-project scope (#482) searches each and unions the hits —
            // scopes are a caller's memberships, so this is a handful of index lookups, not a fan-out.
            IReadOnlyCollection<Guid> searchProjects = filter.ProjectId is { } singleProject
                ? [singleProject]
                : filter.ProjectIds ?? [];

            if (searchProjects.Count == 0)
            {
                return null;
            }

            var idSet = new HashSet<Guid>();
            foreach (var searchProject in searchProjects)
            {
                var hits = await searchService.SearchEntityIdsAsync(
                    searchProject,
                    filter.Query,
                    SearchKind.AgentCall,
                    MaxFulltextHits,
                    cancellationToken);
                idSet.UnionWith(hits);
            }

            if (idSet.Count == 0)
            {
                return null;
            }

            query = query.Where(e => idSet.Contains(e.Id));
        }

        if (!filter.IncludeSystemAgents)
        {
            var nonSystemVersionIds =
                from v in context.Set<AgentVersionEntity>()
                join a in context.Set<AgentEntity>() on v.AgentId equals a.Id
                where !a.IsSystemAgent
                select v.Id;
            query = query.Where(e => nonSystemVersionIds.Contains(e.AgentVersionId));
        }

        return query;
    }

    // Nullable columns sort with a "has value" pre-key so error traces (null latency/usage) land
    // last in BOTH directions instead of Postgres's default nulls-first on DESC. Id tiebreak keeps
    // paging stable across identical values.
    private static IOrderedQueryable<AgentCallEntity> ApplySort(IQueryable<AgentCallEntity> query, AgentCallFilter filter)
    {
        return filter.SortBy switch
        {
            AgentCallSortField.Latency => OrderNullable(query, e => e.LatencyMs == null, e => e.LatencyMs, filter.SortDescending),
            AgentCallSortField.TotalTokens => OrderNullable(query, e => e.TotalTokens == null, e => e.TotalTokens, filter.SortDescending),
            AgentCallSortField.CacheHitRate => OrderNullable(query, e => e.CacheHitRate == null, e => e.CacheHitRate, filter.SortDescending),
            AgentCallSortField.ToolCount => filter.SortDescending
                ? query.OrderByDescending(e => e.ResponseToolRequestCount).ThenByDescending(e => e.Id)
                : query.OrderBy(e => e.ResponseToolRequestCount).ThenBy(e => e.Id),
            _ => filter.SortDescending
                ? query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                : query.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id),
        };
    }

    private static IOrderedQueryable<AgentCallEntity> OrderNullable<TKey>(
        IQueryable<AgentCallEntity> query,
        Expression<Func<AgentCallEntity, bool>> isNull,
        Expression<Func<AgentCallEntity, TKey>> key,
        bool descending)
        => descending
            ? query.OrderBy(isNull).ThenByDescending(key).ThenByDescending(e => e.Id)
            : query.OrderBy(isNull).ThenBy(key).ThenBy(e => e.Id);

    /// <summary>
    /// Returns a dictionary mapping each agent ID to the timestamp of its most recent call across
    /// all versions. Executes as a single grouped aggregate query joining calls to agent versions.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> GetLastCallTimesAsync(
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();

        var result =
            await (from call in context.Set<AgentCallEntity>().AsNoTracking()
                   join version in context.Set<AgentVersionEntity>().AsNoTracking()
                       on call.AgentVersionId equals version.Id
                   group call by version.AgentId
                   into g
                   select new { AgentId = g.Key, LastUsedAt = g.Max(e => e.CreatedAt) })
                .ToDictionaryAsync(x => x.AgentId, x => x.LastUsedAt, cancellationToken);
        return result;
    }

    /// <summary>
    /// Returns the timestamp of the most recent call for the given agent across all its versions, or
    /// null when the agent has no recorded calls. Filters to this agent's version IDs in SQL so the
    /// query stays indexed rather than scanning the full trace table.
    /// </summary>
    public async Task<DateTimeOffset?> GetLastCallTimeAsync(
        Guid agentId,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();

        // Filtered to this agent's versions, so the database scans that agent's calls via the
        // AgentVersionId index instead of grouping the whole trace table the way
        // GetLastCallTimesAsync must. Max over an empty set yields null, which is exactly the
        // "never called" answer — hence the nullable projection rather than a Max on DateTimeOffset.
        var versionIds = context.Set<AgentVersionEntity>()
            .AsNoTracking()
            .Where(v => v.AgentId == agentId)
            .Select(v => v.Id);

        return await context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(c => versionIds.Contains(c.AgentVersionId))
            .Select(c => (DateTimeOffset?)c.CreatedAt)
            .MaxAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the most recent call belonging to the given conversation within the given project, or
    /// null if no matching call exists. Scoped to the project via an agent-version subquery to prevent
    /// cross-project leakage.
    /// </summary>
    public async Task<IAgentCall?> FindLatestByConversationIdAsync(
        Guid conversationId,
        IProject project,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var projectId = project.Id;
        var versionIdsForProject = context.Set<AgentVersionEntity>()
            .Where(v => v.Project == projectId)
            .Select(v => v.Id);
        var stored = await context.Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(e => e.ConversationId == conversationId)
            .Where(e => versionIdsForProject.Contains(e.AgentVersionId))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : await mapper.Map(stored, cancellationToken);
    }

    /// <summary>
    /// Deletes all agent calls created on or before the cutoff date. Executes as a server-side DELETE
    /// on relational providers to avoid materializing rows; falls back to load-then-remove on the
    /// in-memory provider (tests/kiosk) and also loads the Tools navigation to cascade child rows.
    /// </summary>
    public async Task<int> RemoveOlderThanAsync(DateTimeOffset cutoffDate, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<AgentCallEntity>().Where(x => x.CreatedAt <= cutoffDate);

        // ExecuteDelete issues a single server-side DELETE without materializing rows — important
        // for this high-volume table. The in-memory provider (kiosk/tests) can't translate it, so
        // fall back to a materialize-and-remove there.
        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        // Load the Tools navigation so the in-memory provider's client-side cascade also removes the
        // child AgentCallToolEntity rows — it only cascades entities already tracked in the change
        // tracker, and this fallback runs on kiosk/test datasets only, so the extra load is cheap.
        // (Production's relational path above deletes them via the FK's ON DELETE CASCADE.) Return
        // the parent-row count to match ExecuteDeleteAsync — SaveChangesAsync would otherwise also
        // count the cascaded tool rows.
        var toRemove = await query.Include(e => e.Tools).ToListAsync(cancellationToken);
        context.Set<AgentCallEntity>().RemoveRange(toRemove);
        await context.SaveChangesAsync(cancellationToken);
        return toRemove.Count;
    }

    /// <summary>
    /// Returns the session-scoped trace and token deltas for all calls created on or before the
    /// cutoff, grouped by session. Used by the retention sweep to decrement session counters before
    /// deleting the calls.
    /// </summary>
    public async Task<IReadOnlyList<SessionTraceRemoval>> GetSessionRemovalsOlderThanAsync(
        DateTimeOffset cutoffDate,
        CancellationToken cancellationToken = default)
    {
        // Same predicate as RemoveOlderThanAsync, plus the session filter — one indexed CreatedAt
        // range, aggregated server-side into one row per session. TotalTokens is the denormalized
        // column ingestion also bumps the session by, so the delta is an exact reversal; a call with
        // no usage stored contributes 0 rather than dropping out of the count.
        var grouped = await contextFactory()
            .Set<AgentCallEntity>()
            .AsNoTracking()
            .Where(e => e.CreatedAt <= cutoffDate && e.SessionId != null)
            .GroupBy(e => e.SessionId)
            .Select(g => new
            {
                SessionId = g.Key,
                TraceCount = g.Count(),
                TotalTokens = g.Sum(e => (long)(e.TotalTokens ?? 0)),
            })
            .ToListAsync(cancellationToken);

        var removals = new List<SessionTraceRemoval>(grouped.Count);
        foreach (var row in grouped)
        {
            // The grouping key is nullable because the column is; the WHERE above already excluded
            // the null group, so this only ever skips nothing.
            if (row.SessionId is { } sessionId)
                removals.Add(new SessionTraceRemoval(sessionId, row.TraceCount, row.TotalTokens));
        }

        return removals;
    }

    /// <summary>
    /// Bitwise-ORs the given flag into the OutlierFlags column of the specified call, without
    /// overwriting existing flags. Uses a server-side ExecuteUpdate on relational providers to avoid
    /// a read-modify-write race between concurrent statistical writers.
    /// </summary>
    public async Task SetOutlierFlagAsync(Guid id, OutlierFlags flag, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<AgentCallEntity>().Where(x => x.Id == id);

        // ExecuteUpdate performs the bitwise OR server-side in a single primary-key UPDATE, so a
        // concurrent statistical flag write is never lost to a read-modify-write race. The
        // in-memory provider (kiosk/tests) can't translate it, so fall back to load + with-copy
        // there (single-reader review loop — no concurrent writer to race).
        if (context.Database.IsRelational())
        {
            await query.ExecuteUpdateAsync(
                setters => setters.SetProperty(e => e.OutlierFlags, e => e.OutlierFlags | flag),
                cancellationToken);
            return;
        }

        var stored = await query.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (stored is null)
            return;

        context.Set<AgentCallEntity>().Update(stored with { OutlierFlags = stored.OutlierFlags | flag });
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the distinct tool names used in calls belonging to the given project, optionally
    /// scoped to a single agent. Uses the denormalized AgentCallToolEntity rows so the query runs
    /// as an indexed DISTINCT on the (ProjectId, AgentId, ToolName) index without touching the trace table.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetToolNamesAsync(
        Guid projectId, Guid? agentId = null, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<AgentCallToolEntity>()
            .AsNoTracking()
            // Blank names are backfill markers (see AgentCallToolBackfillService), not real tools.
            .Where(t => t.ProjectId == projectId && t.ToolName != string.Empty);

        // Scope to one agent when a filter is active. AgentId is denormalised onto the tool row, so
        // this stays a single-table index-only DISTINCT (the (ProjectId, AgentId, ToolName) index) —
        // no join to the high-volume call table.
        if (agentId.HasValue)
        {
            query = query.Where(t => t.AgentId == agentId.Value);
        }

        return await query
            .Select(t => t.ToolName)
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);
    }
}
