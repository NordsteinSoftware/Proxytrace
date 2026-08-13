using Nordstein.Core.Common.Random;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.Message.Internal;

internal class ConversationGenerator : DomainObjectGenerator<Conversation>
{
    private readonly IDomainObjectGenerator<UserMessage> userMessageGenerator;
    private readonly IDomainObjectGenerator<AssistantMessage> assistantMessageGenerator;

    public ConversationGenerator(
        IRandom random,
        IDomainObjectGenerator<UserMessage> userMessageGenerator,
        IDomainObjectGenerator<AssistantMessage> assistantMessageGenerator) : base(random)
    {
        this.userMessageGenerator = userMessageGenerator;
        this.assistantMessageGenerator = assistantMessageGenerator;
    }

    public override async Task<Conversation> CreateAsync(CancellationToken cancellationToken = default)
    {
        var userMessage = await userMessageGenerator.CreateAsync(cancellationToken);
        var assistantMessage = await assistantMessageGenerator.CreateAsync(cancellationToken);
        return Conversation.Create()
            .With(userMessage)
            .With(assistantMessage);
    }
}
