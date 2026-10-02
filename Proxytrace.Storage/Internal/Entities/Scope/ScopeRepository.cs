using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain;
using Proxytrace.Domain.Scope;
using Proxytrace.Storage.Internal.Entities.AgentVersion;

namespace Proxytrace.Storage.Internal.Entities.Scope;

[UsedImplicitly]
internal class ScopeRepository
    : AbstractRepository<IScope, ScopeEntity>,
      IScopeRepository
{
    private readonly ILogger<ScopeRepository> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeRepository"/> class.
    /// </summary>
    public ScopeRepository(
        IMapper<IScope, ScopeEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        ILogger<ScopeRepository> logger) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Finds the id of a project's scope by its canonical key.
    /// </summary>
    public Task<Guid?> FindIdByKeyAsync(Guid projectId, string externalKey, CancellationToken cancellationToken = default)
        => FindIdByKeyAsync(contextFactory(), projectId, externalKey, cancellationToken);

    /// <summary>
    /// Admit asynchronously.
    /// </summary>
    public async Task<Guid?> AdmitAsync(
        Guid projectId,
        string externalKey,
        int maxScopesPerProject,
        CancellationToken cancellationToken = default)
    {
        var context = contextFactory();

        // Steady state: one probe of the unique (ProjectId, ExternalKey) index — by key, not by the
        // derived id, so a row created under any other id is still found. No in-process cache on
        // purpose — a cached id would outlive a deleted project, scope or test database and then
        // stamp traces with a scope that has no row, which is exactly the drift this check exists to
        // prevent.
        var existingId = await FindIdByKeyAsync(context, projectId, externalKey, cancellationToken);
        if (existingId is not null)
            return existingId;

        if (await context.Set<ScopeEntity>().CountAsync(e => e.ProjectId == projectId, cancellationToken) >= maxScopesPerProject)
            return null;

        var scopeId = ScopeIdDerivation.Derive(projectId, externalKey);
        var now = DateTimeOffset.UtcNow;
        try
        {
            context.Set<ScopeEntity>().Add(new ScopeEntity
            {
                Id = scopeId,
                ExternalKey = externalKey,
                ProjectId = projectId,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await context.SaveChangesAsync(cancellationToken);
            Notify(scopeId, EntityChangeType.Added);
            return scopeId;
        }
        catch (DbUpdateException) when (context.Database.IsRelational())
        {
            // Lost the first-insert race to a concurrent ingester (unique PK / (ProjectId,
            // ExternalKey)): the row exists now, so a lookup on a fresh context finds it — which is
            // why admission must never run inside an ambient transaction, where contextFactory()
            // would hand back the shared, already-aborted context. Null means the insert failed for
            // another reason (e.g. the project was deleted concurrently); best-effort, so the call
            // is ingested unscoped rather than failing.
            var winner = await FindIdByKeyAsync(contextFactory(), projectId, externalKey, cancellationToken);
            if (winner is null)
            {
                logger.LogWarning(
                    "Scope admission lost the insert race but found no row for key {ScopeKey} in project {ProjectId}",
                    externalKey, projectId);
            }
            return winner;
        }
    }

    /// <summary>
    /// Record activity asynchronously.
    /// </summary>
    public Task RecordActivityAsync(
        Guid scopeId,
        Guid agentVersionId,
        long totalTokens,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
        => RecordActivitiesAsync(scopeId, agentVersionId, 1, totalTokens, at, at, cancellationToken);

    /// <summary>
    /// Record activities asynchronously.
    /// </summary>
    public async Task RecordActivitiesAsync(
        Guid scopeId,
        Guid agentVersionId,
        int traceCount,
        long totalTokens,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastSeenAt,
        CancellationToken cancellationToken = default)
    {
        var at = lastSeenAt;
        var context = contextFactory();
        if (context.Database.IsRelational())
        {
            if (await TryBumpAsync(context, scopeId, agentVersionId, traceCount, totalTokens, at, cancellationToken))
                return;
            try
            {
                context.Set<ScopeAgentVersionEntity>().Add(NewMembership(scopeId, agentVersionId, traceCount, totalTokens, firstSeenAt, at));
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Lost the first-insert race (see AdmitAsync): bump on a fresh context instead.
                if (!await TryBumpAsync(contextFactory(), scopeId, agentVersionId, traceCount, totalTokens, at, cancellationToken))
                {
                    logger.LogWarning(
                        "Scope membership upsert lost the insert race but found no row for scope {ScopeId}, version {AgentVersionId}",
                        scopeId, agentVersionId);
                }
            }
            return;
        }

        // In-memory provider (unit tests / kiosk): no ExecuteUpdate support and single-process, so a
        // read-modify-write is race-free enough — mirroring SessionRepository.
        var existing = await context.Set<ScopeAgentVersionEntity>()
            .FirstOrDefaultAsync(e => e.ScopeId == scopeId && e.AgentVersionId == agentVersionId, cancellationToken);
        if (existing is null)
        {
            context.Set<ScopeAgentVersionEntity>().Add(NewMembership(scopeId, agentVersionId, traceCount, totalTokens, firstSeenAt, at));
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(new
            {
                LastSeenAt = at > existing.LastSeenAt ? at : existing.LastSeenAt,
                TraceCount = existing.TraceCount + traceCount,
                TotalTokens = existing.TotalTokens + totalTokens,
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Record trace removals asynchronously.
    /// </summary>
    public async Task RecordTraceRemovalsAsync(
        IReadOnlyCollection<ScopeTraceRemoval> removals,
        CancellationToken cancellationToken = default)
    {
        if (removals.Count == 0)
            return;

        // One row per membership: the set-based UPDATE below joins each target row once, so two
        // removals for the same (scope, version) would otherwise apply only one of their deltas.
        var merged = removals
            .GroupBy(r => (r.ScopeId, r.AgentVersionId))
            .Select(g => new ScopeTraceRemoval(
                g.Key.ScopeId, g.Key.AgentVersionId, g.Sum(r => r.TraceCount), g.Sum(r => r.TotalTokens)))
            .ToArray();

        var context = contextFactory();
        if (context.Database.IsRelational())
        {
            await ApplyRemovalsAsync(context, merged, cancellationToken);
            return;
        }

        // In-memory provider: no raw SQL, single-process — read-modify-write, as RecordActivitiesAsync.
        foreach (var removal in merged)
        {
            var existing = await context.Set<ScopeAgentVersionEntity>()
                .FirstOrDefaultAsync(e => e.ScopeId == removal.ScopeId && e.AgentVersionId == removal.AgentVersionId, cancellationToken);
            if (existing is null)
                continue;

            var traceCount = Math.Max(0, existing.TraceCount - removal.TraceCount);
            if (traceCount == 0)
            {
                context.Set<ScopeAgentVersionEntity>().Remove(existing);
                continue;
            }

            context.Entry(existing).CurrentValues.SetValues(new
            {
                TraceCount = traceCount,
                TotalTokens = Math.Max(0, existing.TotalTokens - removal.TotalTokens),
            });
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Removes memberships last seen at or before the cutoff.
    /// </summary>
    public async Task<int> RemoveMembershipsOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var query = context.Set<ScopeAgentVersionEntity>().Where(e => e.LastSeenAt <= cutoff);

        if (context.Database.IsRelational())
            return await query.ExecuteDeleteAsync(cancellationToken);

        var toRemove = await query.ToListAsync(cancellationToken);
        context.Set<ScopeAgentVersionEntity>().RemoveRange(toRemove);
        await context.SaveChangesAsync(cancellationToken);
        return toRemove.Count;
    }

    /// <summary>
    /// Removes idle, uncurated scopes created at or before the cutoff.
    /// </summary>
    public async Task<int> RemoveIdleOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var memberships = context.Set<ScopeAgentVersionEntity>();
        var query = context.Set<ScopeEntity>()
            .Where(e => e.CreatedAt <= cutoff
                        && e.DisplayName == null
                        && e.Description == null
                        && !memberships.Any(m => m.ScopeId == e.Id));

        // Ids first, for the change notifications the scope pickers listen to — at most the
        // per-project cap per project, so the read is small.
        var ids = await query.Select(e => e.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
            return 0;

        int removed;
        if (context.Database.IsRelational())
        {
            // Re-applies the idle predicate rather than deleting by id, so a scope that gained a
            // member (or a name) since the read above survives.
            removed = await query.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var toRemove = await context.Set<ScopeEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken);
            context.Set<ScopeEntity>().RemoveRange(toRemove);
            await context.SaveChangesAsync(cancellationToken);
            removed = toRemove.Count;
        }

        foreach (var id in ids)
        {
            InvalidateCacheEntry(id);
            Notify(id, EntityChangeType.Removed);
        }
        return removed;
    }

    /// <summary>
    /// Removes a scope. Its traces keep their <c>ScopeId</c> (traces are irreplaceable telemetry
    /// and the column is FK-free); the memberships go with it.
    /// </summary>
    public override async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = await base.RemoveAsync(id, cancellationToken);
        var context = contextFactory();
        if (removed && !context.Database.IsRelational())
        {
            // The FK cascades relationally; the in-memory provider only cascades tracked dependents.
            var orphans = await context.Set<ScopeAgentVersionEntity>().Where(m => m.ScopeId == id).ToListAsync(cancellationToken);
            context.Set<ScopeAgentVersionEntity>().RemoveRange(orphans);
            await context.SaveChangesAsync(cancellationToken);
        }
        return removed;
    }

    /// <summary>
    /// Gets the overviews of all scopes of a project.
    /// </summary>
    public async Task<IReadOnlyList<ScopeOverview>> GetOverviewsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context.Set<ScopeEntity>()
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        if (stored.Count == 0)
            return [];

        var scopeIds = stored.Select(e => e.Id).ToArray();
        var stats = await AgentStatsQuery(context, scopeIds).ToListAsync(cancellationToken);
        var scopes = await Map(stored, cancellationToken);

        return scopes
            .Select(s => ToOverview(s, stats))
            .OrderByDescending(o => o.LastActivityAt ?? o.Scope.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Gets the overview of one scope.
    /// </summary>
    public async Task<ScopeOverview?> GetOverviewAsync(Guid scopeId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var stored = await context.Set<ScopeEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == scopeId, cancellationToken);
        if (stored is null)
            return null;

        var stats = await AgentStatsQuery(context, [scopeId]).ToListAsync(cancellationToken);
        return ToOverview(await mapper.Map(stored, cancellationToken), stats);
    }

    /// <summary>
    /// Gets the ids of the agents that served a scope.
    /// </summary>
    public async Task<IReadOnlySet<Guid>> GetAgentIdsAsync(Guid scopeId, CancellationToken cancellationToken = default)
    {
        var context = contextFactory();
        var agentIds = await context.Set<ScopeAgentVersionEntity>()
            .AsNoTracking()
            .Where(m => m.ScopeId == scopeId)
            .Join(context.Set<AgentVersionEntity>(), m => m.AgentVersionId, v => v.Id, (m, v) => v.AgentId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return agentIds.ToHashSet();
    }

    // Memberships are per version; the overview reports per agent, so versions of one agent fold
    // together here — after a version move they fold into the agent it now belongs to.
    private static IQueryable<ScopeAgentRow> AgentStatsQuery(DbContext context, Guid[] scopeIds)
        => context.Set<ScopeAgentVersionEntity>()
            .AsNoTracking()
            .Where(m => scopeIds.Contains(m.ScopeId))
            .Join(context.Set<AgentVersionEntity>(), m => m.AgentVersionId, v => v.Id, (m, v) => new { m, v.AgentId })
            .GroupBy(x => new { x.m.ScopeId, x.AgentId })
            .Select(g => new ScopeAgentRow(
                g.Key.ScopeId,
                g.Key.AgentId,
                g.Sum(x => x.m.TraceCount),
                g.Sum(x => x.m.TotalTokens),
                g.Min(x => x.m.FirstSeenAt),
                g.Max(x => x.m.LastSeenAt)));

    private static ScopeOverview ToOverview(IScope scope, IReadOnlyList<ScopeAgentRow> rows)
    {
        var agents = rows
            .Where(r => r.ScopeId == scope.Id)
            .Select(r => new ScopeAgentStat(r.AgentId, r.TraceCount, r.TotalTokens, r.FirstSeenAt, r.LastSeenAt))
            .OrderByDescending(a => a.LastSeenAt)
            .ToList();

        return new ScopeOverview(
            scope,
            agents.Sum(a => a.TraceCount),
            agents.Sum(a => a.TotalTokens),
            agents.Count > 0 ? agents.Max(a => a.LastSeenAt) : null,
            agents);
    }

    // One round trip for the whole batch: the deltas travel as parallel arrays and are joined in
    // with unnest, then every membership the batch emptied is deleted. Clamped at zero: a counter
    // can already be low (a bump that failed after its trace persisted — the upsert is best-effort
    // by design), and the clamp then empties the membership early rather than leaving it negative.
    private static Task ApplyRemovalsAsync(
        DbContext context, IReadOnlyCollection<ScopeTraceRemoval> removals, CancellationToken cancellationToken)
    {
        var scopeIds = removals.Select(r => r.ScopeId).ToArray();
        var versionIds = removals.Select(r => r.AgentVersionId).ToArray();
        var traceCounts = removals.Select(r => r.TraceCount).ToArray();
        var tokens = removals.Select(r => r.TotalTokens).ToArray();

        return context.Database.ExecuteSqlAsync(
            $"""
             UPDATE "ScopeAgentVersionEntity" AS m
             SET "TraceCount" = GREATEST(m."TraceCount" - r.trace_count, 0),
                 "TotalTokens" = GREATEST(m."TotalTokens" - r.total_tokens, 0)
             FROM unnest({scopeIds}, {versionIds}, {traceCounts}, {tokens}) AS r(scope_id, version_id, trace_count, total_tokens)
             WHERE m."ScopeId" = r.scope_id AND m."AgentVersionId" = r.version_id;
             DELETE FROM "ScopeAgentVersionEntity" AS m
             USING unnest({scopeIds}, {versionIds}) AS r(scope_id, version_id)
             WHERE m."ScopeId" = r.scope_id AND m."AgentVersionId" = r.version_id AND m."TraceCount" = 0;
             """,
            cancellationToken);
    }

    private static Task<Guid?> FindIdByKeyAsync(
        DbContext context, Guid projectId, string externalKey, CancellationToken cancellationToken)
        => context.Set<ScopeEntity>()
            .AsNoTracking()
            .Where(e => e.ProjectId == projectId && e.ExternalKey == externalKey)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    // LastSeenAt only moves forward: a redelivered or out-of-order ingest must not rewind it. The
    // counters still bump — the trace did arrive.
    private static async Task<bool> TryBumpAsync(
        DbContext context,
        Guid scopeId,
        Guid agentVersionId,
        int traceCount,
        long totalTokens,
        DateTimeOffset at,
        CancellationToken cancellationToken)
        => await context.Set<ScopeAgentVersionEntity>()
            .Where(e => e.ScopeId == scopeId && e.AgentVersionId == agentVersionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.LastSeenAt, e => e.LastSeenAt > at ? e.LastSeenAt : at)
                .SetProperty(e => e.TraceCount, e => e.TraceCount + traceCount)
                .SetProperty(e => e.TotalTokens, e => e.TotalTokens + totalTokens), cancellationToken) > 0;

    private static ScopeAgentVersionEntity NewMembership(
        Guid scopeId, Guid agentVersionId, int traceCount, long totalTokens, DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt)
        => new()
        {
            ScopeId = scopeId,
            AgentVersionId = agentVersionId,
            FirstSeenAt = firstSeenAt,
            LastSeenAt = lastSeenAt,
            TraceCount = traceCount,
            TotalTokens = totalTokens,
        };

    private sealed record ScopeAgentRow(
        Guid ScopeId,
        Guid AgentId,
        int TraceCount,
        long TotalTokens,
        DateTimeOffset FirstSeenAt,
        DateTimeOffset LastSeenAt);
}
