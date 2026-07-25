using Proxytrace.Common.Random;
using Proxytrace.Domain.Internal;
using Proxytrace.Domain.Project;

namespace Proxytrace.Domain.Session.Internal;

internal class SessionGenerator : DomainEntityGenerator<ISession>
{
    private readonly ISession.CreateNew factory;
    private readonly IDomainEntityGenerator<IProject> projectGenerator;

    public SessionGenerator(
        ISession.CreateNew factory,
        IRepository<ISession> repository,
        IDomainEntityGenerator<IProject> projectGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.projectGenerator = projectGenerator;
    }

    public override async Task<ISession> GenerateAsync(CancellationToken cancellationToken = default)
    {
        var project = await projectGenerator.GetOrCreateAsync(cancellationToken);
        return factory(
            externalKey: $"session-{random.Int(1000, 9999)}",
            projectId: project.Id,
            lastActivityAt: DateTimeOffset.UtcNow,
            traceCount: 1,
            totalTokens: 100);
    }
}
