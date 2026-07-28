using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Application.Streaming;
using NSubstitute;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Inference;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.Prompt;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestRunsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_Empty_ReturnsEmptyPage()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    [TestMethod]
    public async Task GetAll_ReturnsSeededRun()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle(r => r.Id == run.Id);
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
    public async Task Get_Existing_ReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var result = await controller.Get(run.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(run.Id);
    }

    [TestMethod]
    public async Task GetCaseFixture_UnknownRun_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetCaseFixture(Guid.NewGuid(), Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task GetCaseFixture_UnknownCase_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var result = await controller.GetCaseFixture(run.Id, Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var run = await services.GetRequiredService<IDomainEntityGenerator<ITestRun>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(run.Id, CancellationToken);

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
    public async Task GetAll_FilterByAgent_ScopesResults()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        var runA = await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);
        var agentId = runA.Group.Suite.Agent.Id;

        var result = await controller.GetAll(agentId: agentId, cancellationToken: CancellationToken);

        result.Items.Should().OnlyContain(r => r.AgentId == agentId);
    }

    [TestMethod]
    public async Task GetAll_Pagination_RespectsPageSize()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var gen = services.GetRequiredService<IDomainEntityGenerator<ITestRun>>();
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);
        await gen.CreateAsync(CancellationToken);

        var firstPage = await controller.GetAll(page: 1, pageSize: 2, cancellationToken: CancellationToken);
        var secondPage = await controller.GetAll(page: 2, pageSize: 2, cancellationToken: CancellationToken);

        firstPage.Items.Should().HaveCount(2);
        firstPage.Total.Should().Be(3);
        secondPage.Items.Should().HaveCount(1);
        secondPage.Items.Select(i => i.Id).Should().NotIntersectWith(firstPage.Items.Select(i => i.Id));
    }

    [TestMethod]
    public async Task Stream_UnknownRun_Returns404()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        await controller.Stream(Guid.NewGuid(), CancellationToken);

        controller.Response.StatusCode.Should().Be(404);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminWithoutAgentFilter_ReturnsOwnProjectsRunsOnly()
    {
        // #482: an unfiltered list from a non-admin used to short-circuit to an empty page, so a
        // REST API key — confined to one project, and with no reason to send an agent filter — was
        // told its own project had no runs.
        IServiceProvider services = GetServices();
        var mine = await SeedRunInNewProjectAsync(services, "mine");
        await SeedRunInNewProjectAsync(services, "theirs");

        var controller = ResolveController(services, ScopedGuard(mine.Group.Suite.Agent.Project.Id));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
        result.Total.Should().Be(1);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminInSeveralProjectsWithoutAgentFilter_ReturnsTheUnion()
    {
        IServiceProvider services = GetServices();
        var first = await SeedRunInNewProjectAsync(services, "first");
        var second = await SeedRunInNewProjectAsync(services, "second");
        await SeedRunInNewProjectAsync(services, "outsider");

        // A member of two projects: the page must be computed over the union of both, not one of
        // them and not the whole instance.
        var controller = ResolveController(
            services,
            ScopedGuard(first.Group.Suite.Agent.Project.Id, second.Group.Suite.Agent.Project.Id));
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Select(i => i.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        result.Total.Should().Be(2);
    }

    [TestMethod]
    public async Task GetAll_AsNonAdminWithoutAccessibleProjects_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        await SeedRunInNewProjectAsync(services, "theirs");

        var controller = ResolveController(services, ScopedGuard());
        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    // A run whose whole chain (project → agent → suite → group → run) is freshly created, so the
    // project id is unique to it and can be handed to ScopedGuard.
    private async Task<ITestRun> SeedRunInNewProjectAsync(IServiceProvider services, string name)
    {
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>()
            .GetOrCreateAsync(CancellationToken);
        var project = await services.GetRequiredService<IProjectRepository>().AddAsync(
            services.GetRequiredService<IProject.CreateNew>()($"P-{name}-{Guid.NewGuid():N}", endpoint, []),
            CancellationToken);

        var agent = await services.GetRequiredService<IAgentRepository>().AddAsync(
            services.GetRequiredService<IAgent.CreateNew>()(
                $"A-{name}",
                services.GetRequiredService<IPromptTemplate.Create>()($"T-{name}", "You are a test agent."),
                [],
                endpoint,
                project,
                services.GetRequiredService<IModelParameters.Create>()(null, null, null, null, null)),
            CancellationToken);

        var suite = await services.GetRequiredService<IRepository<ITestSuite>>().AddAsync(
            services.GetRequiredService<ITestSuite.CreateNew>()($"S-{name}", agent, [], []),
            CancellationToken);
        var group = await services.GetRequiredService<IRepository<ITestRunGroup>>().AddAsync(
            services.GetRequiredService<ITestRunGroup.CreateNew>()(suite, isSystemRun: false, null, sampleCount: 1),
            CancellationToken);

        return await services.GetRequiredService<ITestRunRepository>().AddAsync(
            services.GetRequiredService<ITestRun.CreateNew>()(group, endpoint, sampleIndex: 0),
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

    private static TestRunsController ResolveController(IServiceProvider services)
        => ResolveController(services, services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>());

    private static TestRunsController ResolveController(
        IServiceProvider services, Proxytrace.Api.Auth.IProjectAccessGuard guard) => new(
        services.GetRequiredService<ITestRunRepository>(),
        services.GetRequiredService<IAgentRepository>(),
        services.GetRequiredService<ITestResultBroadcaster>(),
        services.GetRequiredService<TestRunDtoMapper>(),
        guard,
        NullLogger<Audit>.Instance);
}
