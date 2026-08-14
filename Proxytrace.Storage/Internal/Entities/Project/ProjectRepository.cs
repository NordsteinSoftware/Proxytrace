using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nordstein.Core.Common.Text;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Nordstein.Core.Domain.Exceptions;
using Proxytrace.Domain.Project;

namespace Proxytrace.Storage.Internal.Entities.Project;

[UsedImplicitly]
internal class ProjectRepository : AbstractRepository<IProject, ProjectEntity>, IProjectRepository
{
    private readonly ILogger<ProjectRepository> logger;

    public ProjectRepository(
        IMapper<IProject, ProjectEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient,
        ILogger<ProjectRepository> logger) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
        this.logger = logger;
    }

    public async Task<IProject?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var entity = await contextFactory()
            .Set<ProjectEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
        return await Map(entity, cancellationToken);
    }

    public async Task<IProject?> FindBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        // Slugs are derived from the (unique) project name and not stored, so the match can't run
        // in SQL. Names are short and few, so project just the id/name pair and slugify in memory.
        var candidates = await contextFactory()
            .Set<ProjectEntity>()
            .AsNoTracking()
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        // Slugify the incoming value too: a request-path segment keeps its original casing
        // (e.g. "/Development/openai/v1"), so compare canonical slug to canonical slug.
        var normalizedSlug = slug.ToSlug();

        // Project *names* are the unique key, not slugs, and ToSlug is lossy — "My Project",
        // "my-project" and "my_project" all collapse to the same slug. Two legitimately distinct
        // projects can therefore collide here. Order by Id so the winner is stable instead of
        // whatever the query happens to return first (which can flip between requests and silently
        // re-attribute a proxied trace to a different project), and surface the ambiguity.
        var matches = candidates
            .Where(p => p.Name.ToSlug() == normalizedSlug)
            .OrderBy(p => p.Id)
            .ToArray();

        if (matches.Length == 0)
            return null;

        if (matches.Length > 1)
        {
            logger.LogWarning(
                "Project slug '{Slug}' is ambiguous — {Count} projects share it ({Names}). Resolving to the " +
                "lowest id deterministically; rename one of them so proxied traces are attributed unambiguously.",
                normalizedSlug,
                matches.Length,
                string.Join(", ", matches.Select(p => p.Name)));
        }

        return await this.GetAsync(matches[0].Id, cancellationToken);
    }

    public async Task<IReadOnlyList<IProject>> GetByMemberAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var projectIds = await contextFactory()
            .Set<ProjectUserEntity>()
            .AsNoTracking()
            .Where(j => j.UserId == userId)
            .Select(j => j.ProjectId)
            .ToListAsync(cancellationToken);

        return projectIds.Count == 0 ? [] : await GetManyAsync(projectIds, cancellationToken: cancellationToken);
    }

    protected override async Task UpdateRelationsAsync(
        DbContext context,
        ProjectEntity storedEntity,
        CancellationToken cancellationToken)
    {
        var existing = await context.Set<ProjectEntity>()
            .Include(p => p.ProjectUsers)
            .FirstOrDefaultAsync(p => p.Id == storedEntity.Id, cancellationToken);

        if (existing is null)
            throw new EntityNotFoundException(storedEntity.Id, typeof(IProject));

        var newIds = storedEntity.ProjectUsers.Select(j => j.UserId).ToHashSet();
        var existingIds = existing.ProjectUsers.Select(j => j.UserId).ToHashSet();

        var toRemove = existing.ProjectUsers.Where(j => !newIds.Contains(j.UserId)).ToList();
        foreach (var item in toRemove)
            context.Set<ProjectUserEntity>().Remove(item);

        var toAdd = newIds.Except(existingIds)
            .Select(id => new ProjectUserEntity { ProjectId = storedEntity.Id, UserId = id });
        foreach (var item in toAdd)
            context.Set<ProjectUserEntity>().Add(item);
    }
}
