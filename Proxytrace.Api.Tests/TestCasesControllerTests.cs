using Proxytrace.Domain.AuditLog;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.TestSuites;
using Proxytrace.Domain;
using Proxytrace.Domain.TestCase;
using Proxytrace.Domain.TestSuite;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TestCasesControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var services = GetServices();
        var controller = Resolve(services);

        var result = await controller.Get(Guid.NewGuid(), CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Get_Existing_ReturnsDtoWithMessages()
    {
        var services = GetServices();
        var controller = Resolve(services);
        var testCase = await services.GetRequiredService<IDomainEntityGenerator<ITestCase>>().CreateAsync(CancellationToken);

        var result = await controller.Get(testCase.Id, CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be(testCase.Id);
        result.Value.Input.Should().HaveCount(testCase.Input.Messages.Count);
        result.Value.ExpectedOutput.Role.Should().Be("assistant");
    }

    [TestMethod]
    public async Task Update_Unknown_ReturnsNotFound()
    {
        var services = GetServices();
        var controller = Resolve(services);

        var result = await controller.Update(
            Guid.NewGuid(),
            new UpdateTestCaseRequest(new TestSuiteMessageDto("assistant", "anything")),
            CancellationToken);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [TestMethod]
    public async Task Update_WithTextResponse_PersistsNewExpectedOutput()
    {
        var services = GetServices();
        var controller = Resolve(services);
        var testCase = await services.GetRequiredService<IDomainEntityGenerator<ITestCase>>().CreateAsync(CancellationToken);

        var result = await controller.Update(
            testCase.Id,
            new UpdateTestCaseRequest(new TestSuiteMessageDto("assistant", "updated answer")),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.ExpectedOutput.Content.Should().Be("updated answer");

        var reloaded = await controller.Get(testCase.Id, CancellationToken);
        reloaded.Value.Should().NotBeNull();
        reloaded.Value.ExpectedOutput.Content.Should().Be("updated answer");
    }

    [TestMethod]
    public async Task Update_WithToolRequest_PersistsAndReturnsToolRequest()
    {
        var services = GetServices();
        var controller = Resolve(services);
        var testCase = await services.GetRequiredService<IDomainEntityGenerator<ITestCase>>().CreateAsync(CancellationToken);

        var result = await controller.Update(
            testCase.Id,
            new UpdateTestCaseRequest(new TestSuiteMessageDto(
                "assistant",
                "",
                [new ToolRequestInputDto("get_weather", "{\"city\":\"Vienna\"}")])),
            CancellationToken);

        result.Value.Should().NotBeNull();
        result.Value.ExpectedOutput.ToolRequests.Should().ContainSingle()
            .Which.Name.Should().Be("get_weather");

        var reloaded = await controller.Get(testCase.Id, CancellationToken);
        reloaded.Value.Should().NotBeNull();
        reloaded.Value.ExpectedOutput.ToolRequests.Should().ContainSingle()
            .Which.Arguments.Should().Be("{\"city\":\"Vienna\"}");
    }

    private static TestCasesController Resolve(IServiceProvider services) => new(
        services.GetRequiredService<IRepository<ITestCase>>(),
        services.GetRequiredService<ITestSuiteRepository>(),
        services.GetRequiredService<ITestCase.CreateExisting>(),
        services.GetRequiredService<TestSuiteDtoMapper>(),
        services.GetRequiredService<Proxytrace.Api.Auth.IProjectAccessGuard>(),
        NullLogger<Audit>.Instance);
}
