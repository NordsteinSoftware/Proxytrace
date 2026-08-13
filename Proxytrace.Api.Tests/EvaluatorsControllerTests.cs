using Proxytrace.Domain.Statistics;
using Proxytrace.Domain.AuditLog;
using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Evaluators;
using Proxytrace.Api.Evaluators;
using Proxytrace.Application.Evaluator;
using Proxytrace.Domain;
using Proxytrace.Domain.Evaluator;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.TestResult;
using Proxytrace.Domain.TestRun;
using Proxytrace.Domain.TestSuite;
using Proxytrace.Licensing;
using Proxytrace.Licensing.Exceptions;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class EvaluatorsControllerTests : BaseTest<Module>
{
    [TestMethod]
    public void GetAgenticPresets_ReturnsNonEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = controller.GetAgenticPresets();

        result.Should().NotBeEmpty();
    }

    [TestMethod]
    public async Task GetAll_NoEvaluators_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetAll(cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetSummaries_NoEvaluators_ReturnsEmpty()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.GetSummaries(cancellationToken: CancellationToken);

        result.Should().BeEmpty();
    }

    [TestMethod]
    public async Task GetSummaries_ForProject_ReturnsLightItems()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var eval = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);

        var result = await controller.GetSummaries(eval.Project.Id, CancellationToken);

        result.Should().ContainSingle();
        var item = result[0];
        item.Id.Should().Be(eval.Id);
        item.Kind.Should().Be(EvaluatorKind.ExactMatch);
        item.Name.Should().Be(eval.Name);
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
    public async Task Create_ExactMatch_PersistsAndReturnsDto()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateExactMatchEvaluatorRequest { ProjectId = project.Id },
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (EvaluatorDetailDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Kind.Should().Be(EvaluatorKind.ExactMatch);
    }

    [TestMethod]
    public async Task Create_UnknownProject_ReturnsBadRequest()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);

        var result = await controller.Create(
            new CreateExactMatchEvaluatorRequest { ProjectId = Guid.NewGuid() },
            CancellationToken);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestMethod]
    public async Task Create_NumericMatch_Valid_Persists()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateNumericMatchEvaluatorRequest
            {
                ProjectId = project.Id,
                ExtractionPattern = @"(\d+)",
                Tolerance = 0.1m,
            },
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (EvaluatorDetailDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Kind.Should().Be(EvaluatorKind.NumericMatch);
        dto.Tolerance.Should().Be(0.1m);
    }

    [TestMethod]
    public async Task Create_JsonSchemaMatch_Valid_Persists()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

        var result = await controller.Create(
            new CreateJsonSchemaMatchEvaluatorRequest
            {
                ProjectId = project.Id,
                JsonSchema = """{"type":"object"}""",
            },
            CancellationToken);

        var created = (CreatedAtActionResult)(result.Result ?? throw new InvalidOperationException());
        var dto = (EvaluatorDetailDto?)created.Value;
        dto.Should().NotBeNull();
        dto.Kind.Should().Be(EvaluatorKind.JsonSchemaMatch);
    }

    [TestMethod]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        IServiceProvider services = GetServices();
        var controller = ResolveController(services);
        var eval = await services.GetRequiredService<IDomainEntityGenerator<IExactMatchEvaluator>>().CreateAsync(CancellationToken);

        var result = await controller.Delete(eval.Id, CancellationToken);

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
    public async Task BuildAgentic_WhenFeatureNotLicensed_ThrowsFeatureNotLicensed()
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.AgenticEvaluators).Returns(false);
        license.Current.Returns(LicenseSnapshot.Free());

        IServiceProvider services = GetServices(b => b.RegisterInstance(license).As<ILicenseService>());
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var builder = services.GetRequiredService<EvaluatorBuilder>();

        await FluentActions
            .Invoking(() => builder.BuildAsync(
                new CreateAgenticEvaluatorRequest
                {
                    ProjectId = project.Id,
                    Name = "Judge",
                    SystemMessage = "Rate the answer.",
                },
                project,
                CancellationToken))
            .Should().ThrowAsync<FeatureNotLicensedException>()
            .Where(e => e.Feature == LicenseFeature.AgenticEvaluators);
    }

    [TestMethod]
    public async Task BuildAgentic_WhenFeatureLicensed_BuildsEvaluator()
    {
        var license = Substitute.For<ILicenseService>();
        license.IsFeatureEnabled(LicenseFeature.AgenticEvaluators).Returns(true);

        IServiceProvider services = GetServices(b => b.RegisterInstance(license).As<ILicenseService>());
        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);
        var builder = services.GetRequiredService<EvaluatorBuilder>();

        var evaluator = await builder.BuildAsync(
            new CreateAgenticEvaluatorRequest
            {
                ProjectId = project.Id,
                Name = "Judge",
                SystemMessage = "Rate the answer.",
            },
            project,
            CancellationToken);

        evaluator.Should().BeAssignableTo<IAgenticEvaluator>();
    }

    private static EvaluatorsController ResolveController(IServiceProvider services) => new(
        services.GetRequiredService<IEvaluatorRepository>(),
        services.GetRequiredService<IProjectRepository>(),
        services.GetRequiredService<IAgenticEvaluatorPresets>(),
        services.GetRequiredService<ITestResultRepository>(),
        services.GetRequiredService<ITestRunRepository>(),
        services.GetRequiredService<ITestSuiteRepository>(),
        services.GetRequiredService<IEvaluatorStatsReader>(),
        services.GetRequiredService<EvaluatorBuilder>(),
        services.GetRequiredService<EvaluatorDtoMapper>(),
        services.GetRequiredService<ITransaction>(),
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Proxytrace.Domain.AuditLog.Audit>.Instance);
}
