using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.AI.Completions;
using Nordstein.Core.AI.Messages;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelEndpoint;

namespace Proxytrace.Domain.Tests;

[TestClass]
public sealed class AgentCallExtensionsTests : DomainTest<Module>
{
    [TestMethod]
    public async Task TotalTokens_WithUsage_IsInputPlusOutputIncludingCachedInput()
    {
        // Cached input tokens are a subset of the input count, so they are not added again.
        IServiceProvider services = GetServices();
        var call = await CreateCallAsync(services, new TokenUsage(100, 50, cachedInputTokenCount: 30));

        call.TotalTokens().Should().Be(150UL);
        call.CountedTokens().Should().Be(150L);
    }

    [TestMethod]
    public async Task TotalTokens_WithoutResponse_IsUnknownAndCountsAsZero()
    {
        IServiceProvider services = GetServices();
        var call = await CreateCallAsync(services, usage: null);

        call.TotalTokens().Should().BeNull();
        call.CountedTokens().Should().Be(0L);
    }

    private async Task<IAgentCall> CreateCallAsync(IServiceProvider services, TokenUsage? usage)
    {
        var agent = await GetOrCreate<IAgent>(services);
        var endpoint = await GetOrCreate<IModelEndpoint>(services);
        ICompletion? response = usage is null
            ? null
            : services.GetRequiredService<ICompletion.Create>()(
                new AssistantMessage([Content.FromText("ok")], []), usage, TimeSpan.FromSeconds(1));

        return services.GetRequiredService<IAgentCall.CreateNew>()(
            agent: agent, version: agent.CurrentVersion,
            endpoint: endpoint,
            request: Conversation.Create(),
            response: response,
            httpStatus: HttpStatusCode.OK,
            finishReason: null,
            errorMessage: null,
            conversationId: null);
    }
}
