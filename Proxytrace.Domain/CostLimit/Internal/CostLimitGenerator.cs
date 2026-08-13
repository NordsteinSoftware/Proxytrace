using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.CostLimit.Internal;

internal class CostLimitGenerator : DomainEntityGenerator<ICostLimit>
{
    private readonly ICostLimit.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    public CostLimitGenerator(
        ICostLimit.CreateNew factory,
        IRepository<ICostLimit> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    public override async Task<ICostLimit> GenerateAsync(CancellationToken cancellationToken = default)
    {
        IProject project = await projectGenerator.GetOrCreateAsync(cancellationToken);

        return factory(
            project: project,
            agent: null,
            apiKey: null,
            softLimitEur: 50m,
            hardLimitEur: 100m,
            enabled: true);
    }
}
