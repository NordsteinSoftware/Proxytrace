using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.Tools;
using Proxytrace.Application.Statistics;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Session;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

/// <summary>
/// The <c>summary</c> endpoint behind the traces KPI band. Its defining requirement is that it takes
/// the <i>same</i> filter surface as the list — if the two drift, the KPI band starts describing a
/// different set of traces than the table below it.
/// </summary>
[TestClass]
public sealed class AgentCallsControllerSummaryTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetSummary_WhenCallerCannotListTheProject_ReturnsEmptyWithoutQuerying()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        var controller = ResolveController(repo, DenyingGuard());

        var result = await controller.GetSummary(projectId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Count.Should().Be(0);
        result.TotalCostEur.Should().BeNull();
        await repo.DidNotReceiveWithAnyArgs().GetSummaryAsync(default!, default);
    }

    [TestMethod]
    public async Task GetSummary_ForwardsTheFullFilterSurface_SoTheKpisMatchTheTable()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetSummaryAsync(Arg.Any<AgentCallFilter>(), Arg.Any<CancellationToken>())
            .Returns(AgentCallSummary.Empty);
        var controller = ResolveController(repo);

        var agentId = Guid.NewGuid();
        var endpointId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow;

        await controller.GetSummary(
            agentId: agentId,
            endpointId: endpointId,
            model: "gpt-5",
            from: from,
            to: to,
            httpStatus: 404,
            includeSystemAgents: false,
            q: "needle",
            conversationId: conversationId,
            sessionId: sessionId,
            outlierOnly: true,
            anomalyFlags: OutlierFlags.HighLatency,
            httpStatusClass: 5,
            minTokens: 100,
            maxTokens: 5000,
            minLatencyMs: 250,
            maxLatencyMs: 9000,
            toolName: "web_search",
            cancellationToken: CancellationToken);

        await repo.Received(1).GetSummaryAsync(
            Arg.Is<AgentCallFilter>(f =>
                f != null &&
                f.AgentId == agentId &&
                f.EndpointId == endpointId &&
                f.Model == "gpt-5" &&
                f.From == from &&
                f.To == to &&
                f.HttpStatus == 404 &&
                !f.IncludeSystemAgents &&
                f.Query == "needle" &&
                f.ConversationId == conversationId &&
                f.SessionId == sessionId &&
                f.OutlierOnly &&
                f.AnomalyFlags == OutlierFlags.HighLatency &&
                f.HttpStatusClass == 5 &&
                f.MinTokens == 100 &&
                f.MaxTokens == 5000 &&
                f.MinLatencyMs == 250 &&
                f.MaxLatencyMs == 9000 &&
                f.ToolName == "web_search"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetSummary_MapsEveryDomainFieldToTheDto()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetSummaryAsync(Arg.Any<AgentCallFilter>(), Arg.Any<CancellationToken>())
            .Returns(new AgentCallSummary(
                Count: 42,
                InputTokens: 1_000,
                OutputTokens: 500,
                CachedInputTokens: 250,
                TotalCost: 1.25m,
                AvgLatencyMs: 321.5,
                LatencyStdDevMs: 12.25,
                ErrorCount: 7));
        var controller = ResolveController(repo);

        var result = await controller.GetSummary(cancellationToken: CancellationToken);

        result.Count.Should().Be(42);
        result.InputTokens.Should().Be(1_000);
        result.OutputTokens.Should().Be(500);
        result.CachedInputTokens.Should().Be(250);
        result.TotalCostEur.Should().Be(1.25);
        result.AvgLatencyMs.Should().Be(321.5);
        result.LatencyStdDevMs.Should().Be(12.25);
        result.ErrorCount.Should().Be(7);
    }

    [TestMethod]
    public async Task GetSummary_UnknownCost_MapsToNullNotZero()
    {
        // "no matching trace had a known price" and "these traces were free" are different facts,
        // and the KPI tile renders them differently.
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetSummaryAsync(Arg.Any<AgentCallFilter>(), Arg.Any<CancellationToken>())
            .Returns(AgentCallSummary.Empty with { Count = 3, TotalCost = null });
        var controller = ResolveController(repo);

        var result = await controller.GetSummary(cancellationToken: CancellationToken);

        result.TotalCostEur.Should().BeNull();
    }

    // A non-admin who is a member of nothing: every project is inaccessible, scope set is empty.
    private static Proxytrace.Api.Auth.IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    private AgentCallsController ResolveController(IAgentCallRepository repo)
        => ResolveController(repo, GetServices().GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static AgentCallsController ResolveController(
        IAgentCallRepository repo,
        Proxytrace.Api.Auth.IProjectAccessGuard guard)
    {
        var toolDtoMapper = new ToolDtoMapper();
        return new AgentCallsController(
            repo,
            Substitute.For<IAgentRepository>(),
            Substitute.For<ISessionRepository>(),
            Substitute.For<IDashboardStatistics>(),
            Substitute.For<ITraceBroadcaster>(),
            new AgentCallDtoMapper(toolDtoMapper),
            new AgentDtoMapper(toolDtoMapper),
            Substitute.For<IAgentCall.CreateNew>(),
            Substitute.For<ICompletion.Create>(),
            guard,
            NullLogger<Audit>.Instance);
    }
}
