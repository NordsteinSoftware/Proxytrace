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
    /// Admit asynchronously.
    /// </summary>
    public async Task<Guid?> AdmitAsync(
        Guid projectId,
        string externalKey,
        int maxScopesPerProject,
        CancellationToken cancellationToken = default)
    {
        var scopeId = ScopeIdDerivation.Derive(projectId, externalKey);
        var context = contextFactory();

        // Steady state: one primary-key probe. No in-process cache on purpose — a cached id would
        // outlive a deleted project or a test database and then stamp traces with a scope that has
        // no row, which is exactly the drift this check exists to prevent.
        if (await context.Set<ScopeEntity>().AnyAsync(e => e.Id == scopeId, cancellationToken))
            return scopeId;

        var existingKeyId = await FindIdByKeyAsync(context, projectId, externalKey, cancellationToken);
        if (existingKeyId is not null)
            return existingKeyId;

        if (await context.Set<ScopeEntity>().CountAsync(e => e.ProjectId == projectId, cancellationToken) >= maxScopesPerProject)
        {
            logger.LogWarning(
                "Project {ProjectId} reached its limit of {MaxScopes} scopes; ingesting scope key {ScopeKey} unscoped",
                projectId, maxScopesPerProject, externalKey);
            return null;
        }

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

        var context = contextFactory();
        foreach (var removal in removals)
        {
            // Clamped at zero: a counter can already be low (a bump that failed after its trace
            // persisted — the upsert is best-effort by design).
            if (context.Database.IsRelational())
            {
                await context.Set<ScopeAgentVersionEntity>()
                    .Where(e => e.ScopeId == removal.ScopeId && e.AgentVersionId == removal.AgentVersionId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(e => e.TraceCount, e => e.TraceCount > removal.TraceCount ? e.TraceCount - removal.TraceCount : 0)
                        .SetProperty(e => e.TotalTokens, e => e.TotalTokens > removal.TotalTokens ? e.TotalTokens - removal.TotalTokens : 0),
                        cancellationToken);
                continue;
            }

            var existing = await context.Set<ScopeAgentVersionEntity>()
                .FirstOrDefaultAsync(e => e.ScopeId == removal.ScopeId && e.AgentVersionId == removal.AgentVersionId, cancellationToken);
            if (existing is null)
                continue;

            context.Entry(existing).CurrentValues.SetValues(new
            {
                TraceCount = Math.Max(0, existing.TraceCount - removal.TraceCount),
                TotalTokens = Math.Max(0, existing.TotalTokens - removal.TotalTokens),
            });
            await context.SaveChangesAsync(cancellationToken);
        }
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
