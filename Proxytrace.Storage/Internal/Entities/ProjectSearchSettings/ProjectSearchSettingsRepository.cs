using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Proxytrace.Domain;
using Nordstein.Core.Domain.Events;
using Proxytrace.Domain.ProjectSearchSettings;

namespace Proxytrace.Storage.Internal.Entities.ProjectSearchSettings;

[UsedImplicitly]
internal class ProjectSearchSettingsRepository
    : AbstractRepository<IProjectSearchSettings, ProjectSearchSettingsEntity>,
      IProjectSearchSettingsRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchSettingsRepository"/> class.
    /// </summary>
    public ProjectSearchSettingsRepository(
        IMapper<IProjectSearchSettings, ProjectSearchSettingsEntity> mapper,
        Func<StorageDbContext> contextFactory,
        ITransaction transaction,
        IEntityEventService entityEvents,
        AmbientDbContext ambient) : base(mapper, contextFactory, transaction, entityEvents, ambient)
    {
    }

    /// <summary>
    /// Finds the by project asynchronously.
    /// </summary>
    public async Task<IProjectSearchSettings?> FindByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var stored = await contextFactory()
            .Set<ProjectSearchSettingsEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Project == projectId, cancellationToken);

        return stored is null ? null : await mapper.Map(stored, cancellationToken);
    }
}
