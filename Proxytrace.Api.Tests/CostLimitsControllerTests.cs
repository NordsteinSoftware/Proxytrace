using System.Reflection;
using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Costs;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.AuditLog;
using Proxytrace.Domain.CostLimit;
using Proxytrace.Domain.CostLimitBreach;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Licensing;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class CostLimitsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_WithNoBudgets_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);

        IReadOnlyList<CostLimitDto> result = await controller.GetAll(project.Id, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAll_ForInaccessibleProject_ReturnsEmptyRatherThanRevealingExistence()
    {
        IServiceProvider services = GetServices();
        IProject project = await GetProject(services);
        await CreateLimit(services, project, agent: null);

        var guard = Substitute.For<IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        CostLimitsController controller = ResolveController(services, guard);

        IReadOnlyList<CostLimitDto> result = await controller.GetAll(project.Id, CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Create_WithBothThresholds_ReturnsCreatedDto()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);

        var result = await controller.Create(
            new CreateCostLimitRequest(project.Id, null, 50m, 100m), CancellationToken);

        CostLimitDto dto = result.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.Value.Should().BeOfType<CostLimitDto>().Subject;
        dto.ProjectId.Should().Be(project.Id);
        dto.AgentId.Should().BeNull();
        dto.SoftLimitEur.Should().Be(50m);
        dto.HardLimitEur.Should().Be(100m);
        dto.Enabled.Should().BeTrue();
    }

    [TestMethod]
    public async Task Create_WithAgentScope_ReturnsCreatedDtoNamingTheAgent()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IAgent agent = await services.GetRequiredService<IAgentGenerator>().GetOrCreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateCostLimitRequest(agent.Project.Id, agent.Id, null, 25m), CancellationToken);

        // The agent branch is the only one that loads TWO entities while mapping, and it had no
        // coverage at all until an e2e run 500'd on it: the mapper loaded them concurrently, and
        // inside the controller's transaction every repository shares one StorageDbContext (the
        // cache is suppressed while `ambient.IsActive`), so Npgsql raised "A second operation was
        // started on this context instance".
        //
        // NOTE: this test would NOT have caught that. The in-memory provider does not enforce the
        // single-operation guard, so the concurrent version passes here — only the Postgres-backed
        // e2e spec reproduces it. It is kept because the agent-scoped create path deserves coverage
        // on its own; the concurrency invariant is guarded by the comment in CostLimitConfig.Map.
        CostLimitDto dto = result.Result.Should().BeOfType<CreatedAtActionResult>()
            .Which.Value.Should().BeOfType<CostLimitDto>().Subject;
        dto.AgentId.Should().Be(agent.Id);
        dto.AgentName.Should().Be(agent.Name);
        dto.HardLimitEur.Should().Be(25m);
    }

    [TestMethod]
    public async Task Update_OnAgentScopedBudget_PersistsNewThresholds()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IAgent agent = await services.GetRequiredService<IAgentGenerator>().GetOrCreateAsync(CancellationToken);
        ICostLimit limit = await CreateLimit(services, agent.Project, agent);

        // Update maps inside a transaction too, so it shares the create path's hazard.
        var result = await controller.Update(
            limit.Id, new UpdateCostLimitRequest(10m, 200m, true), CancellationToken);

        CostLimitDto dto = result.Value.Should().BeOfType<CostLimitDto>().Subject;
        dto.AgentId.Should().Be(agent.Id);
        dto.HardLimitEur.Should().Be(200m);
    }

    [TestMethod]
    public async Task Create_WithNoThresholds_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);

        var result = await controller.Create(
            new CreateCostLimitRequest(project.Id, null, null, null), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_WithSoftAboveHard_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);

        var result = await controller.Create(
            new CreateCostLimitRequest(project.Id, null, 200m, 100m), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_SecondProjectWideBudget_ReturnsConflict()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);

        await controller.Create(new CreateCostLimitRequest(project.Id, null, 50m, 100m), CancellationToken);
        var second = await controller.Create(
            new CreateCostLimitRequest(project.Id, null, 10m, 20m), CancellationToken);

        second.Result.Should().BeOfType<ConflictObjectResult>();
    }

    [TestMethod]
    public async Task Create_WithAgentOfAnotherProject_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IAgent agent = await services.GetRequiredService<IAgentGenerator>().GetOrCreateAsync(CancellationToken);
        IProject otherProject = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateCostLimitRequest(otherProject.Id, agent.Id, null, 10m), CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_ForInaccessibleProject_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        IProject project = await GetProject(services);

        var guard = Substitute.For<IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        CostLimitsController controller = ResolveController(services, guard);

        var result = await controller.Create(
            new CreateCostLimitRequest(project.Id, null, 50m, 100m), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_ClearsBreachStateSoTheGuardReEvaluates()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);
        ICostLimit limit = await CreateLimit(services, project, agent: null);

        var breaches = services.GetRequiredService<ICostLimitBreachRepository>();
        DateTimeOffset monthStart = CostMonth.StartOf(DateTimeOffset.UtcNow);
        await breaches.AddAsync(
            services.GetRequiredService<ICostLimitBreach.CreateNew>()(
                limit, monthStart, CostThreshold.Hard, 150m),
            CancellationToken);

        await controller.Update(limit.Id, new UpdateCostLimitRequest(100m, 500m, true), CancellationToken);

        // Raising the hard limit must actually lift the block — the breach row is what the proxy reads.
        (await breaches.GetForMonthAsync(monthStart, CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);

        var result = await controller.Update(
            Guid.NewGuid(), new UpdateCostLimitRequest(10m, 20m, true), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_RemovesTheBudget()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);
        IProject project = await GetProject(services);
        ICostLimit limit = await CreateLimit(services, project, agent: null);

        IActionResult result = await controller.Delete(limit.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await services.GetRequiredService<ICostLimitRepository>()
            .GetByProjectAsync(project.Id, CancellationToken)).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Delete_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        CostLimitsController controller = ResolveController(services);

        IActionResult result = await controller.Delete(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    // ── authorization / licensing ─────────────────────────────────────────────

    [TestMethod]
    public void Controller_DoesNotGateReadsBehindALicense()
    {
        // Listing budgets stays free so the Costs page renders identically on every tier.
        typeof(CostLimitsController).GetCustomAttribute<RequiresFeatureAttribute>().Should().BeNull();
    }

    [TestMethod]
    [DataRow(nameof(CostLimitsController.Create))]
    [DataRow(nameof(CostLimitsController.Update))]
    [DataRow(nameof(CostLimitsController.Delete))]
    public void MutatingActions_RequireAdminAndTheCostControlsFeature(string actionName)
    {
        MethodInfo action = typeof(CostLimitsController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"{actionName} not found");

        action.GetCustomAttribute<AuthorizeAttribute>().Should()
            .Match<AuthorizeAttribute>(a => a.Roles == nameof(UserRole.Admin));
        action.GetCustomAttribute<RequiresFeatureAttribute>().Should()
            .Match<RequiresFeatureAttribute>(a => a.Feature == LicenseFeature.CostControls);
    }

    [TestMethod]
    [DataRow(nameof(CostLimitsController.GetAll))]
    [DataRow(nameof(CostLimitsController.Get))]
    public void ReadActions_AreNotAdminOnly(string actionName)
    {
        MethodInfo action = typeof(CostLimitsController).GetMethod(actionName)
            ?? throw new InvalidOperationException($"{actionName} not found");

        action.GetCustomAttribute<AuthorizeAttribute>().Should().BeNull();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<IProject> GetProject(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IProject>>().GetOrCreateAsync(CancellationToken);

    private async Task<ICostLimit> CreateLimit(IServiceProvider services, IProject project, IAgent? agent)
        => await services.GetRequiredService<ICostLimitRepository>().AddAsync(
            services.GetRequiredService<ICostLimit.CreateNew>()(project, agent, 50m, 100m, true),
            CancellationToken);

    private static CostLimitsController ResolveController(
        IServiceProvider services,
        IProjectAccessGuard? accessGuard = null) => new(
        services.GetRequiredService<ICostLimitRepository>(),
        services.GetRequiredService<ICostLimitBreachRepository>(),
        services.GetRequiredService<IProjectRepository>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<ICostLimit.CreateNew>(),
        services.GetRequiredService<ITransaction>(),
        accessGuard ?? services.GetRequiredService<IProjectAccessGuard>(),
        NullLogger<Audit>.Instance);
}
