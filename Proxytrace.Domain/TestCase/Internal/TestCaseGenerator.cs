using Nordstein.Core.Common.Random;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Message;

namespace Proxytrace.Domain.TestCase.Internal;

internal class TestCaseGenerator : DomainEntityGenerator<ITestCase>
{
    private readonly ITestCase.CreateNew factory;
    private readonly IDomainObjectGenerator<Conversation> conversationGenerator;
    private readonly IDomainObjectGenerator<AssistantMessage> assistantMessageGenerator;

    public TestCaseGenerator(
        ITestCase.CreateNew factory,
        IRepository<ITestCase> repository,
        IDomainObjectGenerator<Conversation> conversationGenerator,
        IDomainObjectGenerator<AssistantMessage> assistantMessageGenerator,
        IRandom random) : base(repository, random)
    {
        this.factory = factory;
        this.conversationGenerator = conversationGenerator;
        this.assistantMessageGenerator = assistantMessageGenerator;
    }

    public override async Task<ITestCase> GenerateAsync(CancellationToken cancellationToken = default)
        => factory(
            input: await conversationGenerator.CreateAsync(cancellationToken),
            expectedOutput: await assistantMessageGenerator.CreateAsync(cancellationToken),
            sourceAgentCallId: null);
}
