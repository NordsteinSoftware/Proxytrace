using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Demo;
using Proxytrace.Domain;
using Proxytrace.Domain.Agent;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.Project;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Demo;

[TestClass]
public sealed class KioskEndpointSeedingTests : BaseTest<Module>
{
    private const string RealApiKey = "sk-real-kiosk-key";
    private const string RealBaseUrl = "https://api.example-llm.com/v1";
    private const string RealModel = "demo-gpt";

    private static void RegisterKiosk(ContainerBuilder builder, KioskEndpointOptions endpoint)
    {
        builder.RegisterInstance(new KioskOptions { Enabled = true }).AsSelf();
        builder.RegisterInstance(endpoint).AsSelf();
    }

    private static async Task SeedCoreAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        // Order 0 is CoreSeedScenario; it seeds the project, providers, endpoints and demo agents.
        var core = services.GetServices<IDemoScenario>().OrderBy(s => s.Order).First();
        await core.SeedAsync(cancellationToken);
    }

    [TestMethod]
    public async Task SeedAsync_WhenEndpointConfigured_UsesRealEndpointAsProjectSystemEndpoint()
    {
        IServiceProvider services = GetServices(builder => RegisterKiosk(builder, new KioskEndpointOptions
        {
            BaseUrl = RealBaseUrl,
            ApiKey = RealApiKey,
            Model = RealModel,
            Kind = "OpenAi",
            ProviderName = "Kiosk Provider",
        }));

        await SeedCoreAsync(services, CancellationToken);

        var project = (await services.GetRequiredService<IRepository<IProject>>()
            .GetAllAsync(CancellationToken)).Single();

        project.SystemEndpoint.Provider.ApiKey.Should().Be(RealApiKey);
        project.SystemEndpoint.Provider.Endpoint.Should().Be(new Uri(RealBaseUrl));
        project.SystemEndpoint.Model.Name.Should().Be(RealModel);
    }

    [TestMethod]
    public async Task SeedAsync_WhenEndpointConfigured_RoutesAllDemoAgentsThroughRealEndpoint()
    {
        IServiceProvider services = GetServices(builder => RegisterKiosk(builder, new KioskEndpointOptions
        {
            BaseUrl = RealBaseUrl,
            ApiKey = RealApiKey,
            Model = RealModel,
        }));

        await SeedCoreAsync(services, CancellationToken);

        var agents = await services.GetRequiredService<IRepository<IAgent>>()
            .GetAllAsync(CancellationToken);

        agents.Should().NotBeEmpty();
        agents.Should().OnlyContain(a => a.Endpoint.Provider.ApiKey == RealApiKey);
    }

    [TestMethod]
    public async Task SeedAsync_WhenNoEndpointConfigured_UsesDemoSystemEndpoint()
    {
        IServiceProvider services = GetServices(builder =>
            RegisterKiosk(builder, new KioskEndpointOptions()));

        await SeedCoreAsync(services, CancellationToken);

        var project = (await services.GetRequiredService<IRepository<IProject>>()
            .GetAllAsync(CancellationToken)).Single();

        project.SystemEndpoint.Provider.ApiKey.Should().Be("DEMO-NO-KEY");
    }
}
