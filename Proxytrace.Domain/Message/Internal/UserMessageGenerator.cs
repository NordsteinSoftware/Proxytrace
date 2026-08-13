using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Message.Internal;

internal class UserMessageGenerator : DomainObjectGenerator<UserMessage>
{
    private readonly IDomainObjectGenerator<Content> contentGenerator;

    public UserMessageGenerator(
        IRandom random,
        IDomainObjectGenerator<Content> contentGenerator) : base(random)
    {
        this.contentGenerator = contentGenerator;
    }

    public override async Task<UserMessage> CreateAsync(CancellationToken cancellationToken = default)
    {
        var content = await contentGenerator.CreateAsync(cancellationToken);
        return new UserMessage([content]);
    }
}
