using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Agents;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AgentCall;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class AgentsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_NoAgents_ReturnsEmptyPage()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [TestMethod]
    public async Task GetAll_FiltersByProjectId()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();
        var projectGen = services.GetRequiredService<IDomainEntityGenerator<IProject>>();
        var agentFactory = services.GetRequiredService<IAgent.CreateNew>();
        var agentRepo = services.GetRequiredService<IAgentRepository>();

        var seedA = await agentGen.CreateAsync(CancellationToken);
        var projectB = await projectGen.CreateAsync(CancellationToken);
        var agentB = await agentRepo.AddAsync(
            agentFactory(seedA.Name + "-b", seedA.SystemPrompt, seedA.Tools, seedA.Endpoint, projectB, seedA.ModelParameters),
            CancellationToken);

        var result = await controller.GetAll(projectId: seedA.Project.Id, cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle(a => a.Id == seedA.Id);
        result.Items.Should().NotContain(a => a.Id == agentB.Id);
    }

    [TestMethod]
    public async Task GetAll_ExcludesArchivedAgents()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agentGen = services.GetRequiredService<IDomainEntityGenerator<IAgent>>();

        var kept = await agentGen.CreateAsync(CancellationToken);
        var archived = await agentGen.CreateAsync(CancellationToken);
        // Soft-delete the second agent; it must stay resolvable by id but drop out of the listing.
        await controller.Delete(archived.Id, CancellationToken);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle(a => a.Id == kept.Id);
        result.Items.Should().NotContain(a => a.Id == archived.Id);
    }

    [TestMethod]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_ExistingId_ReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var result = await controller.Get(agent.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(agent.Id);
    }

    [TestMethod]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(agent.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_SystemAgent_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var systemAgent = await services.GetRequiredService<IAgentGenerator>()
            .CreateAsync("System judge", "Judge the response.", isSystemAgent: true);

        var result = await controller.Delete(systemAgent.Id, CancellationToken);

        result.Should().BeOfType<ConflictObjectResult>();
        (await services.GetRequiredService<IAgentRepository>().FindAsync(systemAgent.Id, CancellationToken))
            .Should().NotBeNull();
    }

    [TestMethod]
    public async Task Delete_UserAgent_ArchivesButKeepsItResolvable()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repository = services.GetRequiredService<IAgentRepository>();
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(agent.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await repository.GetByProjectAsync(agent.Project.Id, CancellationToken))
            .Should().NotContain(a => a.Id == agent.Id);
        var retrieved = await repository.GetAsync(agent.Id, CancellationToken);
        retrieved.IsArchived.Should().BeTrue();
    }

    [TestMethod]
    public async Task UpdateEndpoint_SwapsEndpoint()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var agent = await services.GetRequiredService<IDomainEntityGenerator<IAgent>>().CreateAsync(CancellationToken);
        var newEndpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var result = await controller.UpdateEndpoint(agent.Id, new UpdateAgentEndpointRequest(newEndpoint.Id), CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        var reloaded = await services.GetRequiredService<IAgentRepository>().GetAsync(agent.Id, CancellationToken);
        reloaded.Endpoint.Id.Should().Be(newEndpoint.Id);
    }

    private static AgentsController ResolveController(IServiceProvider services) => new(
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<IRepository<IModelEndpoint>>(),
        services.GetRequiredService<IRepository<Proxytrace.Domain.Project.IProject>>(),
        services.GetRequiredService<IAgentCallRepository>(),
        services.GetRequiredService<Proxytrace.Domain.AgentVersion.IAgentVersionRepository>(),
        services.GetRequiredService<IProposalBroadcaster>(),
        services.GetRequiredService<ITheoryBroadcaster>(),
        services.GetRequiredService<AgentDtoMapper>(),
        services.GetRequiredService<Proxytrace.Domain.Agent.IAgent.CreateNew>(),
        services.GetRequiredService<Proxytrace.Domain.Prompt.IPromptTemplate.Create>(),
        services.GetRequiredService<Proxytrace.Domain.Inference.IModelParameters.Create>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance,
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());
}
