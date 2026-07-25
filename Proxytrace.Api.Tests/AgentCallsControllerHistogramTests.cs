using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.AuditLog;
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
using Proxytrace.Domain.Completion;
using Proxytrace.Domain.Session;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AgentCallsControllerHistogramTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetHistogram_MapsBucketsToDto()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        var start = DateTimeOffset.UtcNow;
        repo.GetHistogramAsync(Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new AgentCallHistogramBucket(start, 5, 2)]);
        var controller = ResolveController(repo);

        var result = await controller.GetHistogram(cancellationToken: CancellationToken);

        result.Should().ContainSingle();
        result[0].Start.Should().Be(start);
        result[0].Total.Should().Be(5);
        result[0].Errors.Should().Be(2);
    }

    [TestMethod]
    public async Task GetHistogram_ClampsBucketCount_AndForwardsFilter()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetHistogramAsync(Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var controller = ResolveController(repo);
        var agentId = Guid.NewGuid();

        await controller.GetHistogram(agentId: agentId, buckets: 99999, cancellationToken: CancellationToken);

        await repo.Received(1).GetHistogramAsync(
            Arg.Is<AgentCallFilter>(f => f != null && f.AgentId == agentId),
            240,
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetHistogram_ForwardsTheFullFilterSurface_SoTheTimelineMatchesTheTable()
    {
        var repo = Substitute.For<IAgentCallRepository>();
        repo.GetHistogramAsync(Arg.Any<AgentCallFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var controller = ResolveController(repo);

        await controller.GetHistogram(
            outlierOnly: true,
            anomalyFlags: OutlierFlags.HighLatency,
            httpStatusClass: 5,
            minTokens: 100,
            maxTokens: 5000,
            minLatencyMs: 250,
            maxLatencyMs: 9000,
            toolName: "web_search",
            cancellationToken: CancellationToken);

        await repo.Received(1).GetHistogramAsync(
            Arg.Is<AgentCallFilter>(f =>
                f != null &&
                f.OutlierOnly &&
                f.AnomalyFlags == OutlierFlags.HighLatency &&
                f.HttpStatusClass == 5 &&
                f.MinTokens == 100 &&
                f.MaxTokens == 5000 &&
                f.MinLatencyMs == 250 &&
                f.MaxLatencyMs == 9000 &&
                f.ToolName == "web_search"),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private AgentCallsController ResolveController(IAgentCallRepository repo)
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
            GetServices().GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
            NullLogger<Audit>.Instance);
    }
}
