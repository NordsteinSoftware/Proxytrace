using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.ModelEndpoint;

namespace Proxytrace.Domain.Project.Internal;

internal class ProjectGenerator : DomainEntityGenerator<IProject>
{
    private readonly IProject.CreateNew factory;
    private readonly IDomainEntityGenerator<IModelEndpoint> endpointGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProjectGenerator"/> class.
    /// </summary>
    public ProjectGenerator(
        IProject.CreateNew factory,
        IRepository<IProject> repository,
        IDomainEntityGenerator<IModelEndpoint> endpointGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.endpointGenerator = endpointGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IProject> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var endpoint = await endpointGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            name: random.String(),
            systemEndpoint: endpoint,
            members: []);
    }
}
