using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.ProjectSearchSettings;
using Proxytrace.Domain.Search;

namespace Proxytrace.Application.Search.Internal;

internal sealed class ProjectSearchSettingsResolver : IProjectSearchSettingsResolver
{
    private readonly IProjectSearchSettingsRepository repository;
    private readonly IRepository<IProject> projects;
    private readonly IProjectSearchSettings.CreateNew newFactory;
    private readonly IProjectSearchSettings.CreateExisting existingFactory;
    private readonly SearchConfiguration configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchSettingsResolver"/> class.
    /// </summary>
    public ProjectSearchSettingsResolver(
        IProjectSearchSettingsRepository repository,
        IRepository<IProject> projects,
        IProjectSearchSettings.CreateNew newFactory,
        IProjectSearchSettings.CreateExisting existingFactory,
        SearchConfiguration configuration)
    {
        this.repository = repository;
        this.projects = projects;
        this.newFactory = newFactory;
        this.existingFactory = existingFactory;
        this.configuration = configuration;
    }

    /// <summary>
    /// Gets the or defaults asynchronously.
    /// </summary>
    public async Task<IProjectSearchSettings> GetOrDefaultsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindByProjectAsync(projectId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var project = await projects.GetAsync(projectId, cancellationToken);
        return newFactory(
            project: project,
            enabled: true,
            indexedKinds: Enum.GetValues<SearchKind>(),
            autoReindexOnChange: true,
            snippetLength: configuration.SnippetMaxChars);
    }

    /// <summary>
    /// Upsert asynchronously.
    /// </summary>
    public async Task<IProjectSearchSettings> UpsertAsync(IProjectSearchSettings settings, CancellationToken cancellationToken = default)
    {
        var existing = await repository.FindByProjectAsync(settings.Project.Id, cancellationToken);
        if (existing is null)
        {
            return await repository.AddAsync(settings, cancellationToken);
        }

        // Reuse the existing row's identity so UpdateAsync targets it.
        var rebound = existingFactory(
            project: settings.Project,
            enabled: settings.Enabled,
            indexedKinds: settings.IndexedKinds,
            autoReindexOnChange: settings.AutoReindexOnChange,
            snippetLength: settings.SnippetLength,
            existing: existing);
        return await repository.UpdateAsync(rebound, cancellationToken);
    }
}
