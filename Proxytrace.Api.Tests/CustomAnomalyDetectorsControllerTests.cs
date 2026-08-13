using System.Reflection;
using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Anomalies;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.CustomAnomaly;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class CustomAnomalyDetectorsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_Empty_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        var result = await controller.GetAll(project.Id, CancellationToken);

        result.Should().BeEmpty();
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
    public async Task Create_Valid_ProvisionsHiddenSystemAgentAndReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        var created = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id) with
            {
                Triggers =
                [
                    new AnomalyTriggerDto(TriggerKind.Phrase, "refund"),
                    new AnomalyTriggerDto(TriggerKind.Regex, "escalat(e|ion)"),
                ],
            },
            CancellationToken);

        var dto = created.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.Value.Should().BeOfType<CustomAnomalyDetectorDto>().Subject;
        dto.Name.Should().Be("Refund promises");
        dto.Instructions.Should().Be("Flag turns where the assistant promises a refund.");
        dto.ProjectId.Should().Be(project.Id);
        dto.EndpointId.Should().Be(project.SystemEndpoint.Id);
        dto.Triggers.Should().HaveCount(2);
        dto.AllAgents.Should().BeTrue();
        dto.IsEnabled.Should().BeTrue();

        // The hidden judge agent exists, is a system agent, and carries the instructions as its
        // system prompt.
        var detector = await services.GetRequiredService<ICustomAnomalyDetectorRepository>()
            .GetAsync(dto.Id, CancellationToken);
        detector.Agent.IsSystemAgent.Should().BeTrue();
        detector.Agent.SystemPrompt.Template.Should().Be("Flag turns where the assistant promises a refund.");
    }

    [TestMethod]
    public async Task Create_UnknownProject_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            BuildCreateRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_UnknownEndpoint_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        var result = await controller.Create(
            BuildCreateRequest(project.Id, Guid.NewGuid()), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_NoTriggers_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        var result = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id) with { Triggers = [] },
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_InvalidRegexTrigger_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        // A backreference parses under classic Regex but is rejected by NonBacktracking — the
        // options the review pipeline actually matches with.
        var result = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id) with
            {
                Triggers = [new AnomalyTriggerDto(TriggerKind.Regex, @"(a)\1")],
            },
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_NotAllAgentsWithoutAgentIds_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);

        var result = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id) with { AllAgents = false, AgentIds = [] },
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_AgentFromAnotherProject_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);
        var foreignProject = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .CreateAsync(CancellationToken);
        var foreignAgent = await CreateAgentIn(services, foreignProject);

        var result = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id) with
            {
                AllAgents = false,
                AgentIds = [foreignAgent.Id],
            },
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Update_Existing_PersistsChangesAndUpdatesInstructions()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);
        var scopedAgent = await CreateAgentIn(services, project);
        var dto = await CreateDetector(controller, project);

        var updated = await controller.Update(
            dto.Id,
            new UpdateCustomAnomalyDetectorRequest
            {
                Name = "Renamed",
                Instructions = "New review instructions.",
                Triggers = [new AnomalyTriggerDto(TriggerKind.Phrase, "lawsuit")],
                AllAgents = false,
                AgentIds = [scopedAgent.Id],
                IsEnabled = false,
                BlockUpstream = false,
            },
            CancellationToken);

        var value = updated.Value;
        value.Should().NotBeNull();
        value.Name.Should().Be("Renamed");
        value.Instructions.Should().Be("New review instructions.");
        value.AllAgents.Should().BeFalse();
        value.AgentIds.Should().ContainSingle().Which.Should().Be(scopedAgent.Id);
        value.IsEnabled.Should().BeFalse();

        var reloaded = await services.GetRequiredService<ICustomAnomalyDetectorRepository>()
            .GetAsync(dto.Id, CancellationToken);
        reloaded.Name.Should().Be("Renamed");
        reloaded.Agent.SystemPrompt.Template.Should().Be("New review instructions.");
        reloaded.Triggers.Should().ContainSingle().Which.Pattern.Should().Be("lawsuit");
        reloaded.ScopedAgents.Should().ContainSingle().Which.Id.Should().Be(scopedAgent.Id);
        reloaded.IsEnabled.Should().BeFalse();
    }

    [TestMethod]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateCustomAnomalyDetectorRequest
            {
                Name = "Renamed",
                Instructions = "x",
                Triggers = [new AnomalyTriggerDto(TriggerKind.Phrase, "refund")],
                AllAgents = true,
                IsEnabled = true,
                BlockUpstream = false,
            },
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_Existing_RemovesDetectorAndHiddenAgent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await GetProject(services);
        var dto = await CreateDetector(controller, project);
        var detector = await services.GetRequiredService<ICustomAnomalyDetectorRepository>()
            .GetAsync(dto.Id, CancellationToken);
        var hiddenAgentId = detector.Agent.Id;

        var result = await controller.Delete(dto.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await services.GetRequiredService<ICustomAnomalyDetectorRepository>()
            .ContainsAsync(dto.Id, CancellationToken)).Should().BeFalse();
        var hiddenAgent = await services.GetRequiredService<IAgentRepository>()
            .FindAsync(hiddenAgentId, CancellationToken);
        hiddenAgent.Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── tenant scoping ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Get_WithoutProjectAccess_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var project = await GetProject(services);
        var dto = await CreateDetector(ResolveController(services), project);

        var denied = await DenyAllAccess(services, dto);

        denied.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetAll_WithoutProjectAccess_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var project = await GetProject(services);
        await CreateDetector(ResolveController(services), project);

        var guard = Substitute.For<Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var controller = ResolveController(services, guard);

        var result = await controller.GetAll(project.Id, CancellationToken);

        result.Should().BeEmpty();
    }

    private async Task<ActionResult<CustomAnomalyDetectorDto>> DenyAllAccess(
        IServiceProvider services, CustomAnomalyDetectorDto dto)
    {
        var guard = Substitute.For<Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        var controller = ResolveController(services, guard);
        return await controller.Get(dto.Id, CancellationToken);
    }

    // ── licensing ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Controller_RequiresCustomAnomalyDetectorsFeature()
    {
        var attribute = typeof(CustomAnomalyDetectorsController).GetCustomAttribute<RequiresFeatureAttribute>();

        attribute.Should().NotBeNull();
        attribute.Should().Match<RequiresFeatureAttribute>(a => a.Feature == LicenseFeature.CustomAnomalyDetectors);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<IProject> GetProject(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IProject>>().GetOrCreateAsync(CancellationToken);

    private async Task<IAgent> CreateAgentIn(IServiceProvider services, IProject project)
    {
        var createAgent = services.GetRequiredService<IAgent.CreateNew>();
        var createPrompt = services.GetRequiredService<Proxytrace.Domain.Prompt.IPromptTemplate.Create>();
        var createParameters = services.GetRequiredService<Proxytrace.Domain.Inference.IModelParameters.Create>();
        var agent = createAgent(
            name: $"Agent {Guid.NewGuid():N}",
            systemPrompt: createPrompt("agent", "You help."),
            tools: [],
            endpoint: project.SystemEndpoint,
            project: project,
            modelParameters: createParameters());
        return await agent.AddAsync(CancellationToken);
    }

    private static CreateCustomAnomalyDetectorRequest BuildCreateRequest(Guid projectId, Guid endpointId)
        => new()
        {
            ProjectId = projectId,
            Name = "Refund promises",
            Instructions = "Flag turns where the assistant promises a refund.",
            EndpointId = endpointId,
            Triggers = [new AnomalyTriggerDto(TriggerKind.Phrase, "refund")],
        };

    private async Task<CustomAnomalyDetectorDto> CreateDetector(
        CustomAnomalyDetectorsController controller, IProject project)
    {
        var created = await controller.Create(
            BuildCreateRequest(project.Id, project.SystemEndpoint.Id), CancellationToken);
        return created.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.Value.Should().BeOfType<CustomAnomalyDetectorDto>().Subject;
    }

    private static CustomAnomalyDetectorsController ResolveController(
        IServiceProvider services,
        Api.Auth.IProjectAccessGuard? accessGuard = null) => new(
        services.GetRequiredService<ICustomAnomalyDetectorRepository>(),
        services.GetRequiredService<IProjectRepository>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<IRepository<IModelEndpoint>>(),
        services.GetRequiredService<IAgent.CreateNew>(),
        services.GetRequiredService<ICustomAnomalyDetector.CreateNew>(),
        services.GetRequiredService<Proxytrace.Domain.Prompt.IPromptTemplate.Create>(),
        services.GetRequiredService<Proxytrace.Domain.Inference.IModelParameters.Create>(),
        services.GetRequiredService<ITransaction>(),
        accessGuard ?? services.GetRequiredService<Api.Auth.IProjectAccessGuard>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);
}
