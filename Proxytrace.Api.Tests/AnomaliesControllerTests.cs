using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Nordstein.Core.AI.Completions;
using Proxytrace.Domain.CustomAnomaly;
using Nordstein.Core.AI.Messages;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AnomaliesControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetRecent_Empty_ReturnsEmptyPage()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetRecent(cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [TestMethod]
    public async Task GetRecent_MixedCalls_ReturnsOnlyFlaggedOnes()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await NewAgentAsync(services);
        var flagged = await AddCallAsync(services, agent, OutlierFlags.HighTokens | OutlierFlags.CustomAnomaly);
        await AddCallAsync(services, agent, OutlierFlags.None);

        var result = await controller.GetRecent(cancellationToken: CancellationToken);

        result.Total.Should().Be(1);
        var item = result.Items.Should().ContainSingle().Subject;
        item.Call.Id.Should().Be(flagged.Id);
        item.Call.OutlierFlags.Should().Be((int)(OutlierFlags.HighTokens | OutlierFlags.CustomAnomaly));
        item.CustomAnomalies.Should().BeEmpty("the flag is set but no detector result exists");
    }

    [TestMethod]
    public async Task GetRecent_CallWithDetectorResult_CarriesAttribution()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await NewAgentAsync(services);
        var flagged = await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly);
        var detector = await services
            .GetRequiredService<IDomainEntityGenerator<ICustomAnomalyDetector>>()
            .CreateAsync(CancellationToken);
        var createResult = services.GetRequiredService<ICustomAnomalyResult.CreateNew>();
        await services.GetRequiredService<ICustomAnomalyResultRepository>().AddAsync(
            createResult(detector.Id, flagged.Id, agent.Project.Id, "delete from", "Destructive SQL in user turn."),
            CancellationToken);

        var result = await controller.GetRecent(cancellationToken: CancellationToken);

        var hit = result.Items.Should().ContainSingle().Subject
            .CustomAnomalies.Should().ContainSingle().Subject;
        hit.DetectorId.Should().Be(detector.Id);
        hit.DetectorName.Should().Be(detector.Name);
        hit.MatchedTrigger.Should().Be("delete from");
        hit.Reasoning.Should().Be("Destructive SQL in user turn.");
    }

    [TestMethod]
    public async Task GetRecent_FilteredByAgent_OmitsOtherAgents()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agentA = await NewAgentAsync(services);
        var agentB = await NewAgentAsync(services);
        var flagged = await AddCallAsync(services, agentA, OutlierFlags.HighLatency);
        await AddCallAsync(services, agentB, OutlierFlags.HighLatency);

        var result = await controller.GetRecent(agentId: agentA.Id, cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle().Which.Call.Id.Should().Be(flagged.Id);
    }

    [TestMethod]
    public async Task GetRecent_ClampsPageAndPageSize()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetRecent(page: 0, pageSize: 10_000, cancellationToken: CancellationToken);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(100);
    }

    [TestMethod]
    public async Task GetRecent_AsNonAdminWithoutAccessibleProjectFilter_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        await AddCallAsync(services, agent, OutlierFlags.HighTokens);
        var controller = ResolveController(services, DenyingGuard());

        // No projectId filter + a non-admin scope → no cross-tenant rows leak.
        var result = await controller.GetRecent(cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetForCall_UnknownCall_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetForCall(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.NotFoundResult>();
    }

    [TestMethod]
    public async Task GetForCall_StatisticalOutlierOnly_ReturnsEmptyList()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await NewAgentAsync(services);
        var call = await AddCallAsync(services, agent, OutlierFlags.HighTokens | OutlierFlags.HighLatency);

        var result = await controller.GetForCall(call.Id, CancellationToken);

        result.Value.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetForCall_CustomAnomalyWithResult_ReturnsAttribution()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await NewAgentAsync(services);
        var call = await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly);
        var detector = await services
            .GetRequiredService<IDomainEntityGenerator<ICustomAnomalyDetector>>()
            .CreateAsync(CancellationToken);
        var createResult = services.GetRequiredService<ICustomAnomalyResult.CreateNew>();
        await services.GetRequiredService<ICustomAnomalyResultRepository>().AddAsync(
            createResult(detector.Id, call.Id, agent.Project.Id, "drop table", "Destructive SQL in user turn."),
            CancellationToken);

        var result = await controller.GetForCall(call.Id, CancellationToken);

        var hit = result.Value.Should().ContainSingle().Subject;
        hit.DetectorId.Should().Be(detector.Id);
        hit.DetectorName.Should().Be(detector.Name);
        hit.MatchedTrigger.Should().Be("drop table");
        hit.Reasoning.Should().Be("Destructive SQL in user turn.");
    }

    [TestMethod]
    public async Task GetForCall_CustomAnomalyFlagWithoutResultRow_ReturnsEmptyList()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await NewAgentAsync(services);
        var call = await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly);

        var result = await controller.GetForCall(call.Id, CancellationToken);

        result.Value.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetForCall_InaccessibleProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var agent = await NewAgentAsync(services);
        var call = await AddCallAsync(services, agent, OutlierFlags.CustomAnomaly);
        var controller = ResolveController(services, DenyingGuard());

        // Other tenants' calls are hidden behind a 404, not an empty list.
        var result = await controller.GetForCall(call.Id, CancellationToken);

        result.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.NotFoundResult>();
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

    private static AnomaliesController ResolveController(IServiceProvider services)
        => ResolveController(services, services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static AnomaliesController ResolveController(
        IServiceProvider services, Proxytrace.Api.Auth.IProjectAccessGuard guard) => new(
        services.GetRequiredService<IAgentCallRepository>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<AgentCallDtoMapper>(),
        guard,
        services.GetRequiredService<ICustomAnomalyResultRepository>(),
        services.GetRequiredService<ICustomAnomalyDetectorRepository>());

    private async Task<IAgent> NewAgentAsync(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

    private async Task<IAgentCall> AddCallAsync(IServiceProvider services, IAgent agent, OutlierFlags flags)
    {
        var createCall = services.GetRequiredService<IAgentCall.CreateNew>();
        var createCompletion = services.GetRequiredService<ICompletion.Create>();
        var repo = services.GetRequiredService<IAgentCallRepository>();

        var conversation = Conversation.Create()
            .With(new UserMessage([Content.FromText("hi")]));
        ICompletion completion = createCompletion(
            new AssistantMessage([Content.FromText("ok")], []),
            new TokenUsage(100, 10, 0),
            TimeSpan.FromMilliseconds(100));

        var call = createCall(
            agent: agent,
            version: agent.CurrentVersion,
            endpoint: agent.Endpoint,
            request: conversation,
            response: completion,
            httpStatus: HttpStatusCode.OK,
            finishReason: "stop",
            errorMessage: null,
            modelParameters: agent.ModelParameters,
            outlierFlags: flags);

        return await repo.AddAsync(call, CancellationToken);
    }
}
