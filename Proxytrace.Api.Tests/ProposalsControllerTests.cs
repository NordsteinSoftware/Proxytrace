using Proxytrace.Domain.AuditLog;
using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Proposals;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.OptimizationProposal;
using Proxytrace.Domain.Proposal;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class ProposalsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_NoProposals_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateStatus_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.UpdateStatus(
            Guid.NewGuid(),
            new UpdateProposalStatusRequest(ProposalStatus.Accepted),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetAll_FilteredByUnknownAgent_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(agentId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAll_FilteredByUnknownProject_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(projectId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task UpdateStatus_OnAgent_PersistsStatusChange()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var newEndpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.Medium, "r", newEndpoint, 0.6, 0.7, null, null, [], abRun);
        proposal = (IModelSwitchProposal)await repo.AddAsync(proposal, CancellationToken);

        var target = proposal.Status == ProposalStatus.Accepted ? ProposalStatus.Rejected : ProposalStatus.Accepted;
        await controller.UpdateStatus(proposal.Id, new UpdateProposalStatusRequest(target), CancellationToken);

        var reloaded = await repo.GetAsync(proposal.Id, CancellationToken);
        reloaded.Status.Should().Be(target);
    }

    [TestMethod]
    public async Task GetAll_ModelSwitch_ExposesPassRatesAtTopLevel()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IModelSwitchProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.Medium, "r", endpoint, 0.40, 0.75, null, null, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var result = await controller.GetAll(agentId: agent.Id, cancellationToken: CancellationToken);

        var dto = result.Should().ContainSingle().Subject;
        dto.CurrentPassRate.Should().Be(0.40);
        dto.ProposedPassRate.Should().Be(0.75);
        dto.ExpectedPassRateDelta.Should().BeApproximately(0.35, 1e-9);
    }

    [TestMethod]
    public async Task GetAll_SystemPrompt_ExposesPassRatesAtTopLevel()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.Medium, "r", "new prompt", 0.50, 0.67, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var result = await controller.GetAll(agentId: agent.Id, cancellationToken: CancellationToken);

        var dto = result.Should().ContainSingle().Subject;
        dto.CurrentPassRate.Should().Be(0.50);
        dto.ProposedPassRate.Should().Be(0.67);
        dto.ExpectedPassRateDelta.Should().BeApproximately(0.17, 1e-9);
    }

    [TestMethod]
    public async Task GetAll_ToolUpdate_ExposesPassRatesAtTopLevel()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<IToolUpdateProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.High, "r", [], 0.33, 0.81, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var result = await controller.GetAll(agentId: agent.Id, cancellationToken: CancellationToken);

        var dto = result.Should().ContainSingle().Subject;
        dto.CurrentPassRate.Should().Be(0.33);
        dto.ProposedPassRate.Should().Be(0.81);
        dto.ExpectedPassRateDelta.Should().BeApproximately(0.48, 1e-9);
    }

    [TestMethod]
    public async Task GetAll_NullPassRates_ExposeNullDelta()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.Medium, "r", "msg", null, null, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        var result = await controller.GetAll(agentId: agent.Id, cancellationToken: CancellationToken);

        var dto = result.Should().ContainSingle().Subject;
        dto.CurrentPassRate.Should().BeNull();
        dto.ProposedPassRate.Should().BeNull();
        dto.ExpectedPassRateDelta.Should().BeNull();
    }

    [TestMethod]
    public async Task UpdateStatus_PreservesPassRatesAcrossTransition()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = factory(agent, Priority.Medium, "r", "msg", 0.5, 0.8, [], abRun);
        await repo.AddAsync(proposal, CancellationToken);

        await controller.UpdateStatus(proposal.Id, new UpdateProposalStatusRequest(ProposalStatus.Accepted), CancellationToken);

        var reloaded = await repo.GetAsync(proposal.Id, CancellationToken);
        reloaded.Status.Should().Be(ProposalStatus.Accepted);
        reloaded.CurrentPassRate.Should().Be(0.5);
        reloaded.ProposedPassRate.Should().Be(0.8);
    }

    [TestMethod]
    public async Task UpdateStatus_AcceptedToAdopted_MarksManualAdoption()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = await SeedSystemPromptProposalAsync(services);
        await proposal.Accept(CancellationToken);

        var result = await controller.UpdateStatus(
            proposal.Id, new UpdateProposalStatusRequest(ProposalStatus.Adopted), CancellationToken);

        result.Result.Should().BeOfType<OkObjectResult>();
        var reloaded = await repo.GetAsync(proposal.Id, CancellationToken);
        reloaded.Status.Should().Be(ProposalStatus.Adopted);
        reloaded.AdoptedManually.Should().BeTrue();
        reloaded.AdoptedAgentVersionId.Should().BeNull();
        reloaded.AdoptedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task UpdateStatus_DraftToAdopted_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var proposal = await SeedSystemPromptProposalAsync(services);

        var result = await controller.UpdateStatus(
            proposal.Id, new UpdateProposalStatusRequest(ProposalStatus.Adopted), CancellationToken);

        result.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [TestMethod]
    public async Task UpdateStatus_AcceptedToRejected_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        var proposal = await SeedSystemPromptProposalAsync(services);
        await proposal.Accept(CancellationToken);

        var result = await controller.UpdateStatus(
            proposal.Id, new UpdateProposalStatusRequest(ProposalStatus.Rejected), CancellationToken);

        result.Result.Should().BeOfType<ConflictObjectResult>();
        var reloaded = await repo.GetAsync(proposal.Id, CancellationToken);
        reloaded.Status.Should().Be(ProposalStatus.Accepted);
    }

    [TestMethod]
    public async Task UpdateStatus_PromotesProposal_PublishesStatusChangedEvent()
    {
        var broadcaster = Substitute.For<IProposalBroadcaster>();
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(broadcaster).As<IProposalBroadcaster>());
        var controller = ResolveController(services);
        var proposal = await SeedSystemPromptProposalAsync(services);

        await controller.UpdateStatus(
            proposal.Id, new UpdateProposalStatusRequest(ProposalStatus.Accepted), CancellationToken);

        broadcaster.Received(1).Publish(
            Arg.Is<ProposalEvent>(e => e != null && e.Id == proposal.Id && e is ProposalStatusChangedEvent));
    }

    [TestMethod]
    public async Task GetArtifact_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetArtifact(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetArtifact_ReturnsHandoffPackageWithProposedChangeAndEvidence()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var proposal = await SeedSystemPromptProposalAsync(services, proposedPrompt: "The proposed prompt.");
        await proposal.Accept(CancellationToken);

        var result = await controller.GetArtifact(proposal.Id, CancellationToken);

        var artifact = result.Result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<ProposalArtifactDto>().Subject;
        artifact.SchemaVersion.Should().Be(1);
        artifact.ProposalId.Should().Be(proposal.Id);
        artifact.Kind.Should().Be(ProposalKind.SystemPrompt);
        artifact.Status.Should().Be(ProposalStatus.Accepted);
        artifact.Agent.Id.Should().Be(proposal.Agent.Id);
        artifact.Change.Should().BeOfType<SystemPromptDetailsDto>()
            .Which.ProposedSystemMessage.Should().Be("The proposed prompt.");
        artifact.Adoption.AdoptedAt.Should().BeNull();
    }

    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_ExistingProposal_ReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var proposal = await SeedSystemPromptProposalAsync(services);

        var result = await controller.Get(proposal.Id, CancellationToken);

        var dto = result.Result.Should().BeOfType<OkObjectResult>()
            .Subject.Value.Should().BeOfType<OptimizationProposalDto>().Subject;
        dto.Id.Should().Be(proposal.Id);
        dto.AgentId.Should().Be(proposal.Agent.Id);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminWithoutFilter_ReturnsOwnProjectsProposalsOnly()
    {
        // #482: an unfiltered list from a non-admin used to short-circuit to an empty list, so a
        // REST API key — confined to one project, and with no reason to send a filter — was told its
        // own project had no proposals.
        IServiceProvider services = GetServices();
        var mine = await SeedProposalInNewProjectAsync(services, "mine");
        await SeedProposalInNewProjectAsync(services, "theirs");

        var controller = ResolveController(services, ScopedGuard(mine.Agent.Project.Id));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminInSeveralProjectsWithoutFilter_ReturnsTheUnion()
    {
        IServiceProvider services = GetServices();
        var first = await SeedProposalInNewProjectAsync(services, "first");
        var second = await SeedProposalInNewProjectAsync(services, "second");
        await SeedProposalInNewProjectAsync(services, "outsider");

        var controller = ResolveController(
            services, ScopedGuard(first.Agent.Project.Id, second.Agent.Project.Id));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Select(p => p.Id).Should().BeEquivalentTo([first.Id, second.Id]);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminWithoutAccessibleProjects_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        await SeedProposalInNewProjectAsync(services, "theirs");

        var controller = ResolveController(services, ScopedGuard());
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminFilteredByInaccessibleProject_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var mine = await SeedProposalInNewProjectAsync(services, "mine");
        var theirs = await SeedProposalInNewProjectAsync(services, "theirs");

        var controller = ResolveController(services, ScopedGuard(mine.Agent.Project.Id));
        var result = await controller.GetAll(projectId: theirs.Agent.Project.Id, cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    // A proposal whose agent lives in a freshly created project, so the project id is unique to it
    // and can be handed to ScopedGuard.
    private async Task<IOptimizationProposal> SeedProposalInNewProjectAsync(
        IServiceProvider services, string name)
    {
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<Proxytrace.Domain.Project.IProjectRepository>().AddAsync(
            services.GetRequiredService<Proxytrace.Domain.Project.IProject.CreateNew>()(
                $"P-{name}-{Guid.NewGuid():N}", endpoint, []),
            CancellationToken);

        var agent = await services.GetRequiredService<IAgentRepository>().AddAsync(
            services.GetRequiredService<IAgent.CreateNew>()(
                $"A-{name}",
                services.GetRequiredService<Proxytrace.Domain.Prompt.IPromptTemplate.Create>()(
                    $"T-{name}", "You are a test agent."),
                [],
                endpoint,
                project,
                services.GetRequiredService<Proxytrace.Domain.Inference.IModelParameters.Create>()(
                    null, null, null, null, null)),
            CancellationToken);

        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>()
            .CreateAsync(CancellationToken);
        return await services.GetRequiredService<IOptimizationProposalRepository>().AddAsync(
            services.GetRequiredService<ISystemPromptProposal.CreateNew>()(
                agent, Priority.Medium, "r", $"proposed-{name}", null, null, [], abRun),
            CancellationToken);
    }

    // A non-admin scoped to a specific set of projects: the scope set is non-null (not admin) and
    // contains exactly those projects. No arguments means a member of nothing.
    private static Proxytrace.Api.Auth.IProjectAccessGuard ScopedGuard(params Guid[] projectIds)
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => projectIds.Contains(ci.Arg<Guid>()));
        guard.GetAccessibleProjectIdsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<Guid>?>(projectIds));
        return guard;
    }

    private async Task<IOptimizationProposal> SeedSystemPromptProposalAsync(
        IServiceProvider services, string proposedPrompt = "proposed")
    {
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var abRun = await services.GetRequiredService<IDomainEntityGenerator<Domain.TestRun.ITestRun>>().CreateAsync(CancellationToken);
        var factory = services.GetRequiredService<ISystemPromptProposal.CreateNew>();
        var repo = services.GetRequiredService<IOptimizationProposalRepository>();
        return await repo.AddAsync(
            factory(agent, Priority.Medium, "r", proposedPrompt, null, null, [], abRun),
            CancellationToken);
    }

    private static ProposalsController ResolveController(IServiceProvider services)
        => ResolveController(services, services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static ProposalsController ResolveController(
        IServiceProvider services, Proxytrace.Api.Auth.IProjectAccessGuard guard) => new(
        services.GetRequiredService<IOptimizationProposalRepository>(),
        services.GetRequiredService<IModelSwitchProposal.CreateNew>(),
        services.GetRequiredService<ISystemPromptProposal.CreateNew>(),
        services.GetRequiredService<IToolUpdateProposal.CreateNew>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<IRepository<Proxytrace.Domain.ModelEndpoint.IModelEndpoint>>(),
        services.GetRequiredService<IRepository<Proxytrace.Domain.TestSuite.ITestSuite>>(),
        services.GetRequiredService<IRepository<Proxytrace.Domain.TestRunGroup.ITestRunGroup>>(),
        services.GetRequiredService<IRepository<Proxytrace.Domain.TestRun.ITestRun>>(),
        services.GetRequiredService<Proxytrace.Domain.TestSuite.ITestSuite.CreateNew>(),
        services.GetRequiredService<Proxytrace.Domain.TestRunGroup.ITestRunGroup.CreateNew>(),
        services.GetRequiredService<Proxytrace.Domain.TestRun.ITestRun.CreateNew>(),
        services.GetRequiredService<OptimizationProposalDtoMapper>(),
        services.GetRequiredService<IProposalBroadcaster>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance,
        guard);
}
