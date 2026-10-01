using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Scope.Internal;

internal class ScopeGenerator : DomainEntityGenerator<IScope>
{
    private readonly IScope.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScopeGenerator"/> class.
    /// </summary>
    public ScopeGenerator(
        IScope.CreateNew factory,
        IRepository<IScope> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    /// <summary>
    /// Generates asynchronously.
    /// </summary>
    public override async Task<IScope> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            externalKey: $"scope-{random.Int(1000, 9999)}",
            projectId: project.Id);
    }
}
