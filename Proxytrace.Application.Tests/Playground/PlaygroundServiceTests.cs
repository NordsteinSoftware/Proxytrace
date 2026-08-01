using AwesomeAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Application.Playground.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Exceptions;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Tools;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

namespace Proxytrace.Application.Tests.Playground;

[TestClass]
public sealed class PlaygroundServiceTests : BaseTest<Module>
{
    private static readonly PlaygroundModelParameters EmptyParams =
        new(null, null, null, null, null, null, null, null);

    private static PlaygroundCompleteRequest Request(
        Guid agentId,
        Guid endpointId,
        IReadOnlyList<PlaygroundMessage>? messages = null,
        IReadOnlyList<PlaygroundToolSpecification>? tools = null) =>
        new(agentId, endpointId, "you are helpful", EmptyParams, tools ?? [], messages ?? []);

    private static PlaygroundService Build(
        IAgent? agent,
        IModelEndpoint? endpoint,
        out IRepository<IAgent> agentRepo,
        out IRepository<IModelEndpoint> endpointRepo,
        bool isDevelopment = false)
    {
        agentRepo = Substitute.For<IRepository<IAgent>>();
        endpointRepo = Substitute.For<IRepository<IModelEndpoint>>();
        if (agent is not null)
            agentRepo.GetAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        if (endpoint is not null)
            endpointRepo.GetAsync(endpoint.Id, Arg.Any<CancellationToken>()).Returns(endpoint);
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return new PlaygroundService(agentRepo, endpointRepo, NullLogger<PlaygroundService>.Instance, env);
    }

    private static IModelEndpoint MakeEndpoint(string modelName = "gpt-4o", decimal? inputCost = null, decimal? outputCost = null)
    {
        var model = Substitute.For<IModel>();
        model.Name.Returns(modelName);
        var endpoint = Substitute.For<IModelEndpoint>();
        endpoint.Id.Returns(Guid.NewGuid());
        endpoint.Model.Returns(model);
        endpoint.InputTokenCost.Returns(inputCost);
        endpoint.OutputTokenCost.Returns(outputCost);
        if (inputCost is not null && outputCost is not null)
        {
            endpoint.CalculateCost(Arg.Any<TokenUsage>())
                .Returns(ci =>
                {
                    var u = ci.Arg<TokenUsage>();
                    ArgumentNullException.ThrowIfNull(u);
                    return u.InputTokenCount * inputCost.Value + u.OutputTokenCount * outputCost.Value;
                });
        }
        return endpoint;
    }

    private static IAgent MakeAgent(IModelClient client, IReadOnlyList<ToolSpecification>? tools = null)
    {
        var agent = Substitute.For<IAgent>();
        agent.Id.Returns(Guid.NewGuid());
        agent.Tools.Returns(tools ?? []);
        agent.CreateClient(Arg.Any<IModelEndpoint?>(), Arg.Any<bool>()).Returns(client);
        return agent;
    }

    private static IModelClient MakeStream(IEnumerable<ModelStreamUpdate> updates)
    {
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync(updates));
        return client;
    }

    private static async IAsyncEnumerable<ModelStreamUpdate> ToAsync(IEnumerable<ModelStreamUpdate> updates)
    {
        foreach (var u in updates)
        {
            await Task.Yield();
            yield return u;
        }
    }

    [TestMethod]
    public async Task CompleteStreamAsync_AgentNotFound_YieldsErrorEvent()
    {
        var svc = Build(agent: null, endpoint: null, out var agentRepo, out _);
        var agentId = Guid.NewGuid();
        agentRepo.GetAsync(agentId, Arg.Any<CancellationToken>())
            .Returns<IAgent>(_ => throw new EntityNotFoundException(agentId, typeof(IAgent)));

        var events = await svc.CompleteStreamAsync(Request(agentId, Guid.NewGuid()), CancellationToken).ToListAsync();

        events.Should().ContainSingle().Which.Should().BeOfType<ErrorEvent>();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_EndpointNotFound_YieldsErrorEvent()
    {
        var client = MakeStream([]);
        var agent = MakeAgent(client);
        var svc = Build(agent, endpoint: null, out _, out var endpointRepo);
        var endpointId = Guid.NewGuid();
        endpointRepo.GetAsync(endpointId, Arg.Any<CancellationToken>())
            .Returns<IModelEndpoint>(_ => throw new EntityNotFoundException(endpointId, typeof(IModelEndpoint)));

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpointId), CancellationToken).ToListAsync();

        events.Should().ContainSingle().Which.Should().BeOfType<ErrorEvent>();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_StreamCreationThrows_OutsideDevelopment_SuppressesRawMessage()
    {
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        events.Should().ContainSingle();
        // Outside Development the raw exception message (which may carry SQL/schema/paths) must not leak.
        var message = events[0].Should().BeOfType<ErrorEvent>().Which.Message;
        message.Should().NotContain("boom");
        message.Should().StartWith("An unexpected error occurred");
    }

    [TestMethod]
    public async Task CompleteStreamAsync_StreamCreationThrows_InDevelopment_KeepsRawMessage()
    {
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("boom"));
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _, isDevelopment: true);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        events.Should().ContainSingle();
        events[0].Should().BeOfType<ErrorEvent>().Which.Message.Should().Be("boom");
    }

    [TestMethod]
    public async Task CompleteStreamAsync_TextDelta_YieldsTokenEvent()
    {
        var client = MakeStream([
            new TextDelta("Hel"),
            new TextDelta("lo"),
            new Completed(new TokenUsage(5, 7), TimeSpan.FromMilliseconds(100), "stop"),
        ]);
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        events.OfType<TokenEvent>().Select(e => e.Delta).Should().Equal("Hel", "lo");
        events.Last().Should().BeOfType<DoneEvent>();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_ToolRequested_YieldsToolRequestEvent()
    {
        var toolRequest = new ToolRequest("call_1", "lookup", "{\"q\":\"x\"}");
        var client = MakeStream([
            new ToolRequested(toolRequest),
            new Completed(new TokenUsage(1, 1), TimeSpan.FromMilliseconds(10), "tool_calls"),
        ]);
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        var tool = events.OfType<ToolRequestEvent>().Single();
        tool.Id.Should().Be("call_1");
        tool.Name.Should().Be("lookup");
        tool.Arguments.Should().Be("{\"q\":\"x\"}");
    }

    [TestMethod]
    public async Task CompleteStreamAsync_Completed_YieldsDoneEventWithTokenCountsAndLatency()
    {
        var client = MakeStream([
            new Completed(new TokenUsage(12, 34), TimeSpan.FromMilliseconds(250), "stop"),
        ]);
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        var done = events.OfType<DoneEvent>().Single();
        done.InputTokens.Should().Be(12);
        done.OutputTokens.Should().Be(34);
        done.LatencyMs.Should().Be(250);
        done.FinishReason.Should().Be("stop");
        done.CostEur.Should().BeNull();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_WithEndpointCosts_CalculatesCost()
    {
        var client = MakeStream([
            new Completed(new TokenUsage(10, 20), TimeSpan.FromMilliseconds(50), "stop"),
        ]);
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint(inputCost: 2m, outputCost: 3m);
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        var done = events.OfType<DoneEvent>().Single();
        done.CostEur.Should().Be(10 * 2m + 20 * 3m);
    }

    [TestMethod]
    public async Task CompleteStreamAsync_WithEndpointCostsButZeroTokens_DoesNotCalculateCost()
    {
        var client = MakeStream([
            new Completed(null, TimeSpan.FromMilliseconds(50), "stop"),
        ]);
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint(inputCost: 2m, outputCost: 3m);
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        events.OfType<DoneEvent>().Single().CostEur.Should().BeNull();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_StreamThrowsMidIteration_OutsideDevelopment_SuppressesRawMessage()
    {
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowMidStream());
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        var message = events.Last().Should().BeOfType<ErrorEvent>().Which.Message;
        message.Should().NotContain("stream blew up");
        message.Should().StartWith("An unexpected error occurred");
    }

    [TestMethod]
    public async Task CompleteStreamAsync_StreamThrowsMidIteration_InDevelopment_KeepsRawMessage()
    {
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowMidStream());
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _, isDevelopment: true);

        var events = await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id), CancellationToken).ToListAsync();

        events.Last().Should().BeOfType<ErrorEvent>().Which.Message.Should().Be("stream blew up");
    }

    private static async IAsyncEnumerable<ModelStreamUpdate> ThrowMidStream()
    {
        await Task.Yield();
        yield return new TextDelta("partial");
        throw new InvalidOperationException("stream blew up");
    }

    [TestMethod]
    public async Task CompleteStreamAsync_SystemMessageInPayload_IsIgnored()
    {
        Conversation? captured = null;
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Do<Conversation>(c => captured = c), Arg.Any<ModelOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync([new Completed(new TokenUsage(1, 1), TimeSpan.Zero, "stop")]));
        var agent = MakeAgent(client);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var messages = new List<PlaygroundMessage>
        {
            new("system", "ignore me", [], null, false, null),
            new("user", "hi", [], null, false, null),
        };
        await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id, messages), CancellationToken).ToListAsync();

        captured.Should().NotBeNull();
        captured.Messages.Should().ContainSingle();
        captured.Messages.Single().Should().BeOfType<UserMessage>();
    }

    [TestMethod]
    public async Task CompleteStreamAsync_ToolOverrideMatchingAgentTool_PassedThroughToOptions()
    {
        var agentTool = new ToolSpecification("lookup", "original", new ToolArguments([]));
        ModelOptions? capturedOptions = null;
        var client = Substitute.For<IModelClient>();
        client.StreamAsync(Arg.Any<SystemMessage>(), Arg.Any<Conversation>(), Arg.Do<ModelOptions>(o => capturedOptions = o), Arg.Any<CancellationToken>())
            .Returns(_ => ToAsync([new Completed(new TokenUsage(1, 1), TimeSpan.Zero, "stop")]));
        var agent = MakeAgent(client, [agentTool]);
        var endpoint = MakeEndpoint();
        var svc = Build(agent, endpoint, out _, out _);

        var overrides = new List<PlaygroundToolSpecification>
        {
            new("lookup", "overridden description", []),
            new("nonexistent", "ignored", []),
        };
        await svc.CompleteStreamAsync(Request(agent.Id, endpoint.Id, tools: overrides), CancellationToken).ToListAsync();

        capturedOptions.Should().NotBeNull();
        capturedOptions.Tools.Should().ContainSingle();
        capturedOptions.Tools[0].Name.Should().Be("lookup");
        capturedOptions.Tools[0].Description.Should().Be("overridden description");
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
