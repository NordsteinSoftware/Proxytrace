using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Message;
using Proxytrace.Domain.Session;
using Proxytrace.Domain.Usage;
using Proxytrace.Testing;

namespace Proxytrace.Storage.Tests;

/// <summary>
/// The deltas trace retention needs to keep the denormalized session counters honest (#436): the
/// per-session totals of the calls a cutoff is about to delete, read before the delete removes them.
/// </summary>
[TestClass]
public sealed class SessionTraceRemovalQueryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetSessionRemovalsOlderThanAsync_GroupsTheDoomedCallsBySession()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        await SeedCallAsync(services, agent, sessionA, inputTokens: 10, outputTokens: 5);
        await SeedCallAsync(services, agent, sessionA, inputTokens: 7, outputTokens: 3);
        await SeedCallAsync(services, agent, sessionB, inputTokens: 1, outputTokens: 1);
        await SeedCallAsync(services, agent, sessionId: null, inputTokens: 100, outputTokens: 100);

        // A cutoff in the future covers every seeded call — the same predicate the delete uses.
        var removals = await repo.GetSessionRemovalsOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken);

        removals.Should().HaveCount(2, "the call without a session contributes nothing");
        removals.Should().ContainEquivalentOf(new SessionTraceRemoval(sessionA, 2, 25));
        removals.Should().ContainEquivalentOf(new SessionTraceRemoval(sessionB, 1, 2));
    }

    [TestMethod]
    public async Task GetSessionRemovalsOlderThanAsync_WithNothingPastTheCutoff_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IAgentCallRepository>();

        await SeedCallAsync(services, agent, Guid.NewGuid(), inputTokens: 10, outputTokens: 5);

        var removals = await repo.GetSessionRemovalsOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-1), CancellationToken);

        removals.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetSessionRemovalsOlderThanAsync_CallWithoutUsage_CountsTheTraceWithZeroTokens()
    {
        // A failed call stores no usage. It still occupied a slot in the session's TraceCount, so it
        // must come back out of it — with a zero token delta, not by dropping out of the group.
        IServiceProvider services = GetServices();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var session = Guid.NewGuid();
        await SeedCallAsync(services, agent, session, inputTokens: null, outputTokens: null);

        var removals = await repo.GetSessionRemovalsOlderThanAsync(DateTimeOffset.UtcNow.AddDays(1), CancellationToken);

        removals.Should().ContainEquivalentOf(new SessionTraceRemoval(session, 1, 0));
    }

    private async Task<IAgentCall> SeedCallAsync(
        IServiceProvider services,
        IAgent agent,
        Guid? sessionId,
        ulong? inputTokens,
        ulong? outputTokens)
    {
        var conversationGen = services.GetRequiredService<IDomainObjectGenerator<Conversation>>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var request = await conversationGen.CreateAsync(CancellationToken);

        var usage = inputTokens.HasValue && outputTokens.HasValue
            ? new TokenUsage(inputTokens.Value, outputTokens.Value)
            : null;
        ICompletion response = createCompletion(
            new AssistantMessage([Content.FromText("ok")], []), usage, TimeSpan.FromMilliseconds(50));

        IAgentCall call = services.GetRequiredService<IAgentCall.CreateNew>()(
            agent,
            agent.CurrentVersion,
            agent.Endpoint,
            request,
            response,
            httpStatus: HttpStatusCode.OK,
            finishReason: "stop",
            errorMessage: null,
            modelParameters: agent.ModelParameters,
            conversationId: null,
            sessionId: sessionId);

        return await services.GetRequiredService<IAgentCallRepository>().AddAsync(call, CancellationToken);
    }
}
