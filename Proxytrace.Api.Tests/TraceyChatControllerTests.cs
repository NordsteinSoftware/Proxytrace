using System.Text;
using Autofac;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Application.Auth;
using Proxytrace.Application.Ingestion;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Proxytrace.Domain.User;
using Proxytrace.Messaging;
using Nordstein.Core.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class TraceyChatControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Forward_WritesUpstreamResponse_AndIngestsInProcess()
    {
        var upstreamBody = FakeHttpMessageHandler.BuildOpenAiResponse("Hello from Tracey");
        var ingestion = Substitute.For<IIngestionExecutor>();

        IServiceProvider services = GetServices(builder =>
        {
            builder.Register(_ => new FakeHttpClientFactory(upstreamBody))
                .As<IHttpClientFactory>().SingleInstance();
            builder.RegisterInstance(ingestion).As<IIngestionExecutor>();
        });

        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .CreateAsync(CancellationToken);

        var responseBody = await InvokeForwardAsync(
            services, AdminUserAccessor(services), project.Id,
            """{"model":"gpt-4o-mini","messages":[{"role":"user","content":"hi"}]}""");

        responseBody.Should().Be(upstreamBody);
        await ingestion.Received(1).IngestAsync(
            Arg.Is<IngestMessage>(m =>
                m != null
                && m.ProjectId == project.Id
                && m.ProviderId == project.SystemEndpoint.Provider.Id
                && m.HttpStatus == 200
                && m.AgentName == "Tracey"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Forward_UnknownProject_Returns404()
    {
        IServiceProvider services = GetServices(builder =>
            builder.RegisterInstance(Substitute.For<IIngestionExecutor>()).As<IIngestionExecutor>());

        var controller = new TraceyChatController(
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IIngestionExecutor>(),
            services.GetRequiredService<IRepository<IProject>>(),
            services.GetRequiredService<Proxytrace.Application.Tracey.ITraceyAgentProvisioner>(),
            AdminUserAccessor(services),
            NullLogger<TraceyChatController>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        http.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        await controller.Forward(Guid.NewGuid(), "chat/completions", CancellationToken);

        http.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [TestMethod]
    public async Task Forward_NonMember_Returns403()
    {
        var ingestion = Substitute.For<IIngestionExecutor>();
        IServiceProvider services = GetServices(builder =>
        {
            builder.Register(_ => new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("x")))
                .As<IHttpClientFactory>().SingleInstance();
            builder.RegisterInstance(ingestion).As<IIngestionExecutor>();
        });

        var project = await services.GetRequiredService<IDomainEntityGenerator<IProject>>()
            .CreateAsync(CancellationToken);

        // A non-admin user that is not a member of the project.
        var outsider = services.GetRequiredService<IUser.CreateNew>()(
            "outsider@example.com", externalSubject: null, passwordHash: "hash", UserRole.Member);
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(outsider);

        var controller = new TraceyChatController(
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IIngestionExecutor>(),
            services.GetRequiredService<IRepository<IProject>>(),
            services.GetRequiredService<Proxytrace.Application.Tracey.ITraceyAgentProvisioner>(),
            accessor,
            NullLogger<TraceyChatController>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        http.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        await controller.Forward(project.Id, "chat/completions", CancellationToken);

        http.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        await ingestion.DidNotReceive().IngestAsync(Arg.Any<IngestMessage>(), Arg.Any<CancellationToken>());
    }

    private static ICurrentUserAccessor AdminUserAccessor(IServiceProvider services)
    {
        var admin = services.GetRequiredService<IUser.CreateNew>()(
            "admin@example.com", externalSubject: null, passwordHash: "hash", UserRole.Admin);
        var accessor = Substitute.For<ICurrentUserAccessor>();
        accessor.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(admin);
        return accessor;
    }

    private async Task<string> InvokeForwardAsync(
        IServiceProvider services, ICurrentUserAccessor currentUser, Guid projectId, string requestJson)
    {
        var controller = new TraceyChatController(
            services.GetRequiredService<IHttpClientFactory>(),
            services.GetRequiredService<IIngestionExecutor>(),
            services.GetRequiredService<IRepository<IProject>>(),
            services.GetRequiredService<Proxytrace.Application.Tracey.ITraceyAgentProvisioner>(),
            currentUser,
            NullLogger<TraceyChatController>.Instance);

        var http = new DefaultHttpContext();
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestJson));
        http.Request.ContentType = "application/json";
        var responseStream = new MemoryStream();
        http.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext { HttpContext = http };

        await controller.Forward(projectId, "chat/completions", CancellationToken);

        return Encoding.UTF8.GetString(responseStream.ToArray());
    }
}
