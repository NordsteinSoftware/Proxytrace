using AwesomeAssertions;
using Autofac;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Api.Controllers;
using Proxytrace.Api.Dto.Playground;
using Proxytrace.Application.Playground;
using Proxytrace.Application.Playground.Internal;
using Proxytrace.Domain.Agent;
using Proxytrace.Testing;

namespace Proxytrace.Api.Tests;

[TestClass]
public sealed class PlaygroundControllerTests : BaseTest<Module>
{
    [TestMethod]
    public async Task Complete_NotImplemented_WritesErrorEvent()
    {
        var service = Substitute.For<IPlaygroundService>();
        service.CompleteStreamAsync(Arg.Any<PlaygroundCompleteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowNotImplementedAsync());

        IServiceProvider services = GetServices(b => b.RegisterInstance(service).As<IPlaygroundService>());
        var controller = NewController(services.GetRequiredService<IPlaygroundService>());
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.Complete(BuildRequest(), CancellationToken);

        body.Position = 0;
        var text = new StreamReader(body).ReadToEnd();
        text.Should().Contain("event: error");
        text.Should().Contain("not implemented");
    }

    [TestMethod]
    public async Task Complete_UnexpectedException_OutsideDevelopment_SuppressesRawMessage()
    {
        var service = Substitute.For<IPlaygroundService>();
        service.CompleteStreamAsync(Arg.Any<PlaygroundCompleteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowAsync(new InvalidOperationException("boom")));

        IServiceProvider services = GetServices(b => b.RegisterInstance(service).As<IPlaygroundService>());
        var controller = NewController(services.GetRequiredService<IPlaygroundService>());
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.Complete(BuildRequest(), CancellationToken);

        body.Position = 0;
        var text = new StreamReader(body).ReadToEnd();
        text.Should().Contain("event: error");
        // Outside Development the raw exception message (which may carry SQL/schema/paths) must not leak.
        text.Should().NotContain("boom");
        text.Should().Contain("An unexpected error occurred");
    }

    [TestMethod]
    public async Task Complete_UnexpectedException_InDevelopment_KeepsRawMessage()
    {
        var service = Substitute.For<IPlaygroundService>();
        service.CompleteStreamAsync(Arg.Any<PlaygroundCompleteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => ThrowAsync(new InvalidOperationException("boom")));

        IServiceProvider services = GetServices(b => b.RegisterInstance(service).As<IPlaygroundService>());
        var controller = NewController(services.GetRequiredService<IPlaygroundService>(), isDevelopment: true);
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.Complete(BuildRequest(), CancellationToken);

        body.Position = 0;
        var text = new StreamReader(body).ReadToEnd();
        text.Should().Contain("event: error");
        text.Should().Contain("boom");
    }

    [TestMethod]
    public async Task Complete_WhenAgentInaccessible_Returns404AndDoesNotStream()
    {
        var service = Substitute.For<IPlaygroundService>();
        IServiceProvider services = GetServices(b => b.RegisterInstance(service).As<IPlaygroundService>());

        var agents = Substitute.For<IAgentRepository>();
        agents.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var controller = new PlaygroundController(
            services.GetRequiredService<IPlaygroundService>(), agents, DenyingGuard(),
            NullLogger<PlaygroundController>.Instance, Env(isDevelopment: false));
        var httpContext = new DefaultHttpContext();
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        await controller.Complete(BuildRequest(), CancellationToken);

        httpContext.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Length.Should().Be(0); // the stream never opened — no SSE body written
        service.DidNotReceiveWithAnyArgs().CompleteStreamAsync(default!, default);
    }

    // Resolve a playground controller whose agent + guard permit the request, so the SSE path runs.
    private static PlaygroundController NewController(IPlaygroundService service, bool isDevelopment = false)
    {
        var agents = Substitute.For<IAgentRepository>();
        agents.GetProjectIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return new PlaygroundController(
            service, agents, guard, NullLogger<PlaygroundController>.Instance, Env(isDevelopment));
    }

    private static Proxytrace.Api.Auth.IProjectAccessGuard DenyingGuard()
    {
        var guard = Substitute.For<Proxytrace.Api.Auth.IProjectAccessGuard>();
        guard.CanAccessProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return guard;
    }

    private static IWebHostEnvironment Env(bool isDevelopment)
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns(isDevelopment ? "Development" : "Production");
        return env;
    }

    private static PlaygroundCompleteRequestDto BuildRequest() => new(
        Guid.NewGuid(), Guid.NewGuid(),
        "system",
        new PlaygroundModelParametersDto(null, null, null, null, null, null, null, null),
        [],
        []);

    private static async IAsyncEnumerable<PlaygroundEvent> ThrowNotImplementedAsync()
    {
        await Task.Yield();
        throw new NotImplementedException();
#pragma warning disable CS0162
        // ReSharper disable once HeuristicUnreachableCode
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<PlaygroundEvent> ThrowAsync(Exception ex)
    {
        await Task.Yield();
        throw ex;
#pragma warning disable CS0162
        // ReSharper disable once HeuristicUnreachableCode
        yield break;
#pragma warning restore CS0162
    }
}
