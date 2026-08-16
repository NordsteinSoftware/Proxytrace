using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Search;

namespace Proxytrace.Domain.ProjectSearchSettings.Internal;

internal class ProjectSearchSettingsGenerator : DomainEntityGenerator<IProjectSearchSettings>
{
    private readonly IProjectSearchSettings.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectSearchSettingsGenerator"/> class.
    /// </summary>
    public ProjectSearchSettingsGenerator(
        IProjectSearchSettings.CreateNew factory,
        IRepository<IProjectSearchSettings> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IProjectSearchSettings> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var project = await projectGenerator.CreateAsync(cancellationToken);
        return factory(
            project: project,
            enabled: true,
            indexedKinds: Enum.GetValues<SearchKind>(),
            autoReindexOnChange: true,
            snippetLength: 160);
    }
}
