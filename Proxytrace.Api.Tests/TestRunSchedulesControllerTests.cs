using Proxytrace.Domain.AuditLog;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Auth.Licensing;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestRuns;
using Proxytrace.Application.TestRun;
using Proxytrace.Domain;
using Proxytrace.Domain.ModelEndpoint;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestRunGroup;
using Proxytrace.Domain.TestRunSchedule;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestRunSchedulesControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetAll_Empty_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

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
    public async Task Create_UnknownSuite_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            new CreateTestRunScheduleRequest("Nightly", Guid.NewGuid(), [Guid.NewGuid()], 60, true),
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
            new CreateTestRunScheduleRequest("Nightly", suite.Id, [], 60, true),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_ZeroInterval_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateTestRunScheduleRequest("Nightly", suite.Id, [endpoint.Id], 0, true),
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
            new CreateTestRunScheduleRequest(
                "Nightly", suite.Id,
                [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 60, true),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Update_MoreThanThreeEndpoints_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var schedule = await services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>().CreateAsync(CancellationToken);

        var result = await controller.Update(
            schedule.Id,
            new UpdateTestRunScheduleRequest(
                "Renamed",
                [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], 30, false),
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_Valid_ThenListReturnsScheduleWithEmptyRecentRuns()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var suite = await services.GetRequiredService<IDomainEntityGenerator<ITestSuite>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        var created = await controller.Create(
            new CreateTestRunScheduleRequest("Nightly", suite.Id, [endpoint.Id], 90, true),
            CancellationToken);

        created.Result.Should().BeOfType<CreatedAtActionResult>();

        var list = await controller.GetAll(cancellationToken: CancellationToken);

        var dto = list.Should().ContainSingle().Subject;
        dto.Name.Should().Be("Nightly");
        dto.IntervalMinutes.Should().Be(90);
        dto.IsEnabled.Should().BeTrue();
        dto.SuiteId.Should().Be(suite.Id);
        dto.Endpoints.Should().ContainSingle().Which.Id.Should().Be(endpoint.Id);
        dto.RecentRuns.Should().NotBeNull();
        dto.RecentRuns.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateTestRunScheduleRequest("Renamed", [Guid.NewGuid()], 30, false),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_Existing_PersistsChanges()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var schedule = await services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>().CreateAsync(CancellationToken);
        var endpoint = await services.GetRequiredService<IDomainEntityGenerator<IModelEndpoint>>().CreateAsync(CancellationToken);

        await controller.Update(
            schedule.Id,
            new UpdateTestRunScheduleRequest("Renamed", [endpoint.Id], 15, false),
            CancellationToken);

        var reloaded = await repo.GetAsync(schedule.Id, CancellationToken);
        reloaded.Name.Should().Be("Renamed");
        reloaded.Interval.Should().Be(TimeSpan.FromMinutes(15));
        reloaded.IsEnabled.Should().BeFalse();
        reloaded.Endpoints.Should().ContainSingle().Which.Id.Should().Be(endpoint.Id);
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
    public async Task Delete_Existing_ReturnsNoContentAndRemoves()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var repo = services.GetRequiredService<ITestRunScheduleRepository>();
        var schedule = await services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(schedule.Id, CancellationToken);

        result.Should().BeOfType<NoContentResult>();
        (await repo.ContainsAsync(schedule.Id, CancellationToken)).Should().BeFalse();
    }

    [TestMethod]
    public async Task RunNow_Unknown_ReturnsNotFound()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.RunNow(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task RunNow_Existing_ReturnsDtoAndTagsGroupWithSchedule()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var groups = services.GetRequiredService<ITestRunGroupRepository>();
        var schedule = await services.GetRequiredService<IDomainEntityGenerator<ITestRunSchedule>>().CreateAsync(CancellationToken);

        var result = await controller.RunNow(schedule.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Should().Match<TestRunScheduleDto>(v => v.Id == schedule.Id);

        var scheduledGroups = await groups.GetByScheduleAsync(schedule.Id, 5, CancellationToken);
        scheduledGroups.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Create_Endpoint_RequiresScheduledTestRunsFeature()
    {
        var method = typeof(TestRunSchedulesController).GetMethod(nameof(TestRunSchedulesController.Create))
            ?? throw new InvalidOperationException("Create method not found");
        var attribute = method.GetCustomAttribute<RequiresFeatureAttribute>();
        attribute.Should().NotBeNull();
        attribute.Should().Match<RequiresFeatureAttribute>(a => a.Feature == LicenseFeature.ScheduledTestRuns);
    }

    [TestMethod]
    public async Task LicenseFilter_CreateWithoutFeature_ReturnsPaymentRequired()
    {
        var service = Substitute.For<ILicenseService>();
        service.IsFeatureEnabled(Arg.Any<LicenseFeature>()).Returns(false);
        service.Current.Returns(new LicenseSnapshot(
            LicenseTier.Free, LicenseStatus.Free, null, null, null, null,
            new HashSet<LicenseFeature>(), new Dictionary<LicenseLimit, long>()));
        var filter = new LicenseEnforcementFilter(service);
        var context = BuildContext(new RequiresFeatureAttribute(LicenseFeature.ScheduledTestRuns));

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status402PaymentRequired);
    }

    [TestMethod]
    public async Task LicenseFilter_CreateWithFeature_DoesNotShortCircuit()
    {
        var service = Substitute.For<ILicenseService>();
        service.IsFeatureEnabled(LicenseFeature.ScheduledTestRuns).Returns(true);
        var filter = new LicenseEnforcementFilter(service);
        var context = BuildContext(new RequiresFeatureAttribute(LicenseFeature.ScheduledTestRuns));

        await filter.OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    private static AuthorizationFilterContext BuildContext(params object[] endpointMetadata)
    {
        var actionContext = new ActionContext(
            new DefaultHttpContext(),
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = endpointMetadata });
        return new AuthorizationFilterContext(actionContext, []);
    }

    private static TestRunSchedulesController ResolveController(IServiceProvider services) => new(
        services.GetRequiredService<ITestRunScheduleRepository>(),
        services.GetRequiredService<ITestRunGroupRepository>(),
        services.GetRequiredService<ITestRunRepository>(),
        services.GetRequiredService<ITestSuiteRepository>(),
        services.GetRequiredService<Proxytrace.Domain.Agent.IAgentRepository>(),
        services.GetRequiredService<IRepository<IModelEndpoint>>(),
        services.GetRequiredService<ITestRunSchedule.CreateNew>(),
        services.GetRequiredService<ITestRunnerService>(),
        services.GetRequiredService<TestRunDtoMapper>(),
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
        NullLogger<Audit>.Instance);
}
