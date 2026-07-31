using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.AgentCalls;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Api.Dto.TestCases;
using Proxytrace.Application.Statistics;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.TestCase;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AgentCallsControllerProposalsTests : BaseTest<Module>
{
    [TestMethod]
    public async Task ProposeTestCases_ReturnsTheServicesProposals()
    {
        IServiceProvider services = GetServices();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>()
            .CreateAsync(CancellationToken);
        var synthesis = SynthesisReturning(new TestCaseProposalSet
        {
            Summary = "a refund conversation",
            Proposals =
            [
                new TestCaseProposal
                {
                    AgentCallId = call.Id,
                    Kind = ProposalKind.Promotion,
                    Title = "Checks the order",
                    Rationale = "because",
                    Relevance = ProposalRelevance.High,
                },
            ],
            Skipped = [],
        });
        var controller = ResolveController(services, synthesis);

        var result = await controller.ProposeTestCases(
            call.Id, new SynthesizeTestCasesRequest(), CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Summary.Should().Be("a refund conversation");
        result.Value.Proposals.Should().ContainSingle().Which.AgentCallId.Should().Be(call.Id);
    }

    [TestMethod]
    public async Task ProposeTestCases_ForAnUnknownCall_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services, SynthesisReturning(TestCaseProposalSet.Empty));

        var result = await controller.ProposeTestCases(
            Guid.NewGuid(), new SynthesizeTestCasesRequest(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task ProposeTestCases_ForAnotherTenantsCall_ReturnsNotFound()
    {
        // A 404 rather than a 403: an id must not be an existence oracle.
        IServiceProvider services = GetServices();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>()
            .CreateAsync(CancellationToken);
        var synthesis = SynthesisReturning(TestCaseProposalSet.Empty);
        var controller = ResolveController(services, synthesis, DenyingGuard());

        var result = await controller.ProposeTestCases(
            call.Id, new SynthesizeTestCasesRequest(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
        await synthesis.DidNotReceiveWithAnyArgs().SynthesizeAsync(
            default!, default, default!, default, default);
    }

    [TestMethod]
    public async Task ProposeTestCases_TrimsTheRoundsToTheCap()
    {
        IServiceProvider services = GetServices();
        var call = await services.GetRequiredService<IDomainEntityGenerator<IAgentCall>>()
            .CreateAsync(CancellationToken);
        var synthesis = SynthesisReturning(TestCaseProposalSet.Empty);
        var controller = ResolveController(services, synthesis);
        var emptySet = new TestCaseProposalSetDto("", [], [], null);
        var rounds = Enumerable.Range(0, TestCaseProposalSet.MaxRounds + 3)
            .Select(i => new SynthesisRoundDto($"r{i}", emptySet))
            .ToArray();

        await controller.ProposeTestCases(
            call.Id, new SynthesizeTestCasesRequest(Rounds: rounds), CancellationToken);

        await synthesis.Received(1).SynthesizeAsync(
            Arg.Any<IAgentCall>(),
            Arg.Any<ITestSuite?>(),
            Arg.Is<IReadOnlyList<SynthesisRound>>(
                list => list != null && list.Count == TestCaseProposalSet.MaxRounds),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public void ProposeTestCases_Endpoint_RequiresTheTestCaseSynthesisFeature()
    {
        var method = typeof(AgentCallsController).GetMethod(nameof(AgentCallsController.ProposeTestCases))
            ?? throw new InvalidOperationException("ProposeTestCases method not found");

        var attribute = method.GetCustomAttribute<RequiresFeatureAttribute>();

        attribute.Should().NotBeNull();
        attribute.Should().Match<RequiresFeatureAttribute>(a => a.Feature == LicenseFeature.TestCaseSynthesis);
    }

    private static ITestCaseSynthesisService SynthesisReturning(TestCaseProposalSet result)
    {
        var synthesis = Substitute.For<ITestCaseSynthesisService>();
        synthesis.SynthesizeAsync(
                Arg.Any<IAgentCall>(), Arg.Any<ITestSuite?>(), Arg.Any<IReadOnlyList<SynthesisRound>>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        return synthesis;
    }

    /// <summary>A non-admin who is a member of nothing: every project is inaccessible.</summary>
    private static Proxytrace.Api.Auth.IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>([]));
        return guard;
    }

    private static AgentCallsController ResolveController(
        IServiceProvider services,
        ITestCaseSynthesisService synthesis)
        => ResolveController(
            services, synthesis, services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static AgentCallsController ResolveController(
        IServiceProvider services,
        ITestCaseSynthesisService synthesis,
        Proxytrace.Api.Auth.IProjectAccessGuard guard) => new(
        services.GetRequiredService<IAgentCallRepository>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<Proxytrace.Domain.Session.ISessionRepository>(),
        services.GetRequiredService<IDashboardStatistics>(),
        services.GetRequiredService<ITraceBroadcaster>(),
        services.GetRequiredService<AgentCallDtoMapper>(),
        services.GetRequiredService<AgentDtoMapper>(),
        services.GetRequiredService<Proxytrace.Domain.AgentCall.IAgentCall.CreateNew>(),
        services.GetRequiredService<Proxytrace.Domain.Completion.ICompletion.Create>(),
        guard,
        NullLogger<Audit>.Instance,
        services.GetRequiredService<ITestSuiteRepository>(),
        synthesis,
        services.GetRequiredService<TestCaseProposalDtoMapper>());
}
