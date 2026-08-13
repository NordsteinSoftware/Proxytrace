using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.TestCase;
using Nordstein.Core.Testing;

namespace Proxytrace.Storage.Tests;

[TestClass]
public sealed class TestCaseRepositoryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Persist_CorrectionCase_RoundTripsSourceAgentCallId()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCase>>();
        var createCorrection = services.GetRequiredService<ITestCase.CreateCorrection>();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>().CreateAsync(CancellationToken);

        var testCase = createCorrection(call, new AssistantMessage([Content.FromText("corrected")], []));
        await repository.AddAsync(testCase, CancellationToken);

        // Reload from storage rather than trusting the in-memory object — proves the EF column + mapper
        // round-trip the provenance id.
        var reloaded = await repository.GetAsync(testCase.Id, CancellationToken);
        reloaded.SourceAgentCallId.Should().Be(call.Id);
    }

    [TestMethod]
    public async Task Persist_SyntheticCase_HasNullSourceAgentCallId()
    {
        IServiceProvider services = GetServices();
        var repository = services.GetRequiredService<IRepository<ITestCase>>();
        var createNew = services.GetRequiredService<ITestCase.CreateNew>();

        var testCase = createNew(Conversation.Create(), new AssistantMessage([Content.FromText("hi")], []), null);
        await repository.AddAsync(testCase, CancellationToken);

        var reloaded = await repository.GetAsync(testCase.Id, CancellationToken);
        reloaded.SourceAgentCallId.Should().BeNull();
    }
}
