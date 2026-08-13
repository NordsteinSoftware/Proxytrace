using Nordstein.Core.Common.Async;
using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Message.Internal;

internal class ContentGenerator : DomainObjectGenerator<Content>
{
    public ContentGenerator(IRandom random) : base(random)
    {
    }

    public override Task<Content> CreateAsync(CancellationToken cancellationToken = default)
        => Content.FromText(random.String()).ToTaskResult();
}
