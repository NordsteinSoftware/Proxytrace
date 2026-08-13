using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Tracey;
using Proxytrace.Application.Tracey.Internal;
using Proxytrace.Domain;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests;

[TestClass]
public sealed class TraceyServiceTests : BaseTest<Module>
{
    [TestMethod]
    public async Task EnsureTraceyAgentAsync_WhenAbsent_CreatesSystemAgent()
    {
        IServiceProvider services = GetServices(RegisterTracey);
        var project = await CreateProjectAsync(services);
        var provisioner = services.GetRequiredService<ITraceyAgentProvisioner>();

        var agent = await provisioner.EnsureTraceyAgentAsync(project, CancellationToken);

        agent.Name.Should().Be(TraceyAgentProvisioner.AgentName);
        agent.IsSystemAgent.Should().BeTrue();
        agent.Project.Id.Should().Be(project.Id);
        // Seeded identity-only: empty tools; the live prompt + tools are captured from Tracey's
        // first call via name-based attribution.
        agent.Tools.Should().BeEmpty();
    }

    [TestMethod]
    public async Task EnsureTraceyAgentAsync_WhenCalledTwice_IsIdempotent()
    {
        IServiceProvider services = GetServices(RegisterTracey);
        var project = await CreateProjectAsync(services);
        var provisioner = services.GetRequiredService<ITraceyAgentProvisioner>();

        var first = await provisioner.EnsureTraceyAgentAsync(project, CancellationToken);
        var second = await provisioner.EnsureTraceyAgentAsync(project, CancellationToken);

        second.Id.Should().Be(first.Id);
    }

    [TestMethod]
    public async Task CreateSessionAsync_ReturnsModelAndTraceyAgent()
    {
        IServiceProvider services = GetServices(RegisterTracey);
        var project = await CreateProjectAsync(services);
        var sessionService = services.GetRequiredService<ITraceySessionService>();
        var provisioner = services.GetRequiredService<ITraceyAgentProvisioner>();
        var traceyAgent = await provisioner.EnsureTraceyAgentAsync(project, CancellationToken);

        var session = await sessionService.CreateSessionAsync(project, CancellationToken);

        session.Model.Should().Be(project.SystemEndpoint.Model.Name);
        session.AgentId.Should().Be(traceyAgent.Id);
    }

    private async Task<IProject> CreateProjectAsync(IServiceProvider services)
        => await services.GetRequiredService<IDomainEntityGenerator<IProject>>().CreateAsync(CancellationToken);

    private static void RegisterTracey(ContainerBuilder builder)
    {
        builder.RegisterType<TraceyAgentProvisioner>().As<ITraceyAgentProvisioner>().SingleInstance();
        builder.RegisterType<TraceySessionService>().As<ITraceySessionService>().SingleInstance();
    }
}
