using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Application.Search.Internal.Mappers;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Search;

/// <summary>
/// Guards issue #236: system agents (Tracey, the prompt/A-B optimizers, agentic-evaluator agents)
/// and the traces they make are internal plumbing and must never reach the title-bar search index.
/// The mappers enforce this by returning null from GetDocument, which both keeps them out of the
/// index and purges any already-indexed entry on the next update/reindex.
/// </summary>
[TestClass]
public sealed class SystemEntitySearchExclusionTests : BaseTest<Module>
{
    [TestMethod]
    public async Task AgentMapper_BuildAsync_ForSystemAgent_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agentRepository = services.GetRequiredService<IRepository<IAgent>>();

        var systemAgent = await agentGenerator.CreateAsync(
            "Tracey", systemPrompt: "internal", isSystemAgent: true, cancellationToken: CancellationToken);

        var mapper = new AgentDocumentMapper(agentRepository, NullLogger<AgentCallDocumentMapper>.Instance);

        var document = await mapper.BuildAsync(systemAgent.Id, CancellationToken);

        document.Should().BeNull();
    }

    [TestMethod]
    public async Task AgentMapper_BuildAsync_ForNormalAgent_ReturnsDocument()
    {
        IServiceProvider services = GetServices();
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var agentRepository = services.GetRequiredService<IRepository<IAgent>>();

        var normalAgent = await agentGenerator.CreateAsync(
            "My Agent", systemPrompt: "be helpful", isSystemAgent: false, cancellationToken: CancellationToken);

        var mapper = new AgentDocumentMapper(agentRepository, NullLogger<AgentCallDocumentMapper>.Instance);

        var document = await mapper.BuildAsync(normalAgent.Id, CancellationToken);

        document.Should().NotBeNull();
    }

    [TestMethod]
    public async Task AgentCallMapper_BuildAsync_ForSystemAgentCall_ReturnsNull()
    {
        IServiceProvider services = GetServices();
        var agentGenerator = services.GetRequiredService<IAgentGenerator>();
        var callFactory = services.GetRequiredService<IAgentCall.CreateNew>();
        var completionFactory = services.GetRequiredService<ICompletion.Create>();
        var callRepository = services.GetRequiredService<IRepository<IAgentCall>>();

        var systemAgent = await agentGenerator.CreateAsync(
            "Optimizer", systemPrompt: "internal", isSystemAgent: true, cancellationToken: CancellationToken);

        var response = completionFactory(
            new AssistantMessage([Content.FromText("internal reasoning")], []),
            new TokenUsage(10, 5),
            TimeSpan.FromSeconds(1));
        var call = callFactory(
            agent: systemAgent,
            version: systemAgent.CurrentVersion,
            endpoint: systemAgent.Endpoint,
            request: Conversation.Create(),
            response: response,
            httpStatus: HttpStatusCode.OK,
            finishReason: "stop",
            errorMessage: null,
            conversationId: null);
        await callRepository.AddAsync(call, CancellationToken);

        var mapper = new AgentCallDocumentMapper(callRepository, NullLogger<AgentCallDocumentMapper>.Instance);

        var document = await mapper.BuildAsync(call.Id, CancellationToken);

        document.Should().BeNull();
    }

    [TestMethod]
    public async Task AgentCallMapper_BuildAsync_ForNormalAgentCall_ReturnsDocument()
    {
        IServiceProvider services = GetServices();
        var callGenerator = services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>();
        var callRepository = services.GetRequiredService<IRepository<IAgentCall>>();

        // The generic generator builds the call around a freshly created, non-system agent.
        var call = await callGenerator.CreateAsync(CancellationToken);
        call.Agent.IsSystemAgent.Should().BeFalse();

        var mapper = new AgentCallDocumentMapper(callRepository, NullLogger<AgentCallDocumentMapper>.Instance);

        var document = await mapper.BuildAsync(call.Id, CancellationToken);

        document.Should().NotBeNull();
    }
}
