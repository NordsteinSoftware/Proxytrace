using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Usage;

namespace Proxytrace.Domain.Completion.Internal;

internal class CompletionGenerator : DomainObjectGenerator<ICompletion>
{
    private readonly IDomainObjectGenerator<AssistantMessage> messageGenerator;
    private readonly IDomainObjectGenerator<TokenUsage> usageGenerator;
    private readonly ICompletion.Create factory;

    public CompletionGenerator(
        IDomainObjectGenerator<AssistantMessage> messageGenerator,
        IDomainObjectGenerator<TokenUsage> usageGenerator,
        ICompletion.Create factory,
        IRandom random) : base(random)
    {
        this.messageGenerator = messageGenerator;
        this.usageGenerator = usageGenerator;
        this.factory = factory;
    }

    public override async Task<ICompletion> CreateAsync(CancellationToken cancellationToken = default)
    {
        AssistantMessage message = await messageGenerator.CreateAsync(cancellationToken);
        TokenUsage usage = await usageGenerator.CreateAsync(cancellationToken);
        return factory(message, usage, random.TimeSpan());
    }
}
