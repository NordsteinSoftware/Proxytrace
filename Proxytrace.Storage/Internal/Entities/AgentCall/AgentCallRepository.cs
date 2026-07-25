using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AgentVersion;
using Proxytrace.Domain.Events;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Search;
using Proxytrace.Domain.Usage;
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
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await Map(stored, cancellationToken);
        return (items, total);
    }

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
            .Skip((page - 1) * pageSize)
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
                rows.Select(r => r.AgentVersionId).Distinct().ToArray(), cancellationToken, ignoreMissing: true))
            .ToDictionary(v => v.Id);
        var agentsById = (await agents.GetManyAsync(
                versionsById.Values.Select(v => v.AgentId).Distinct().ToArray(), cancellationToken, ignoreMissing: true))
            .ToDictionary(a => a.Id);
        var endpointsById = (await endpoints.GetManyAsync(
                rows.Select(r => r.EndpointId).Distinct().ToArray(), cancellationToken, ignoreMissing: true))
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
    /// Builds the filtered (but unpaged, unordered) query shared by list + histogram reads.
    /// Returns <see langword="null"/> when the filter provably matches nothing (e.g. a fulltext
    /// query with no hits, or a fulltext query without a project scope).
    /// </summary>
    private async Task<IQueryable<AgentCallEntity>?> BuildFilteredQueryAsync(
        StorageDbContext context,
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

        if (filter.EndpointId is not null)
        {
            query = query.Where(e => e.EndpointId == filter.EndpointId);
        }

        if (filter.ConversationId.HasValue)
        {
            query = query.Where(e => e.ConversationId == filter.ConversationId.Value);
        }

        if (filter.SessionId.HasValue)
        {
            query = query.Where(e => e.SessionId == filter.SessionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Model))
        {
            var search = filter.Model;
            var matchingEndpointIds = context.Set<ModelEndpointEntity>()
                .Where(me => context.Set<ModelEntity>()
                    .Any(m => m.Id == me.Model && EF.Functions.Like(m.Name, $"%{search}%")))
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
            if (filter.ProjectId is null)
            {
                return null;
            }

            var matchingIds = await searchService.SearchEntityIdsAsync(
                filter.ProjectId.Value,
                filter.Query,
                SearchKind.AgentCall,
                MaxFulltextHits,
                cancellationToken);

            if (matchingIds.Count == 0)
            {
                return null;
            }

            var idSet = matchingIds.ToHashSet();
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
