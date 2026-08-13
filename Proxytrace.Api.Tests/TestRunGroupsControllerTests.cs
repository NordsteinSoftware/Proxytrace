using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Application.Optimization;
using Proxytrace.Application.Streaming;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestRunGroupsControllerTests : BaseTest<Module>
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
    public async Task Get_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Create_UnknownSuite_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            new CreateTestRunGroupRequest(Guid.NewGuid(), [Guid.NewGuid()]),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_NoEndpoints_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateTestRunGroupRequest(suite.Id, []),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_MoreThanThreeEndpoints_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateTestRunGroupRequest(
                suite.Id,
                [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Optimize_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Optimize(Guid.NewGuid(), CancellationToken);

        result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Cancel_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Cancel(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
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
    public async Task Delete_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var group = await services.GetRequiredService<IDomainEntityGenerator<ITestRunGroup>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(group.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
    }

    [TestMethod]
    public async Task GetAll_FilteredByUnknownAgent_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(agentId: Guid.NewGuid(), cancellationToken: CancellationToken);

        result.Items.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetAll_FilteredBySuite_ReturnsOnlyThatSuitesGroups()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repo = services.GetRequiredService<ITestRunGroupRepository>();
        var factory = services.GetRequiredService<ITestRunGroup.CreateNew>();
        var suiteGen = services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>();
        var suiteA = await suiteGen.CreateAsync(CancellationToken);
        var suiteB = await suiteGen.CreateAsync(CancellationToken);
        await repo.AddAsync(factory(suiteA, isSystemRun: false, null, sampleCount: 1), CancellationToken);
        await repo.AddAsync(factory(suiteB, isSystemRun: false, null, sampleCount: 1), CancellationToken);

        var result = await controller.GetAll(suiteId: suiteA.Id, cancellationToken: CancellationToken);

        result.Items.Should().ContainSingle();
        result.Items.Single().SuiteId.Should().Be(suiteA.Id);
    }

    private static TestRunGroupsController ResolveController(IServiceProvider services) => new(
        services.GetRequiredService<ITestRunGroupRepository>(),
        services.GetRequiredService<ITestRunRepository>(),
        services.GetRequiredService<ITestSuiteRepository>(),
        services.GetRequiredService<IRepository<IModelEndpoint>>(),
        services.GetRequiredService<ITestRunnerService>(),
        services.GetRequiredService<ITestResultBroadcaster>(),
        services.GetRequiredService<IOptimizerService>(),
        services.GetRequiredService<TestRunDtoMapper>(),
        services.GetRequiredService<Proxytrace.Domain.Agent.IAgentRepository>(),
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);
}
