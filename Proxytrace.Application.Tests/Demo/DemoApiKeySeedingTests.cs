using Autofac;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Proxytrace.Application.Demo.Scenarios;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Kiosk;
using Nordstein.Core.Testing;

namespace Proxytrace.Application.Tests.Demo;

/// <summary>
/// The kiosk showcase seeds a fixed ingestion API key for the "Showcase Project" so a sample client
/// can authenticate its OpenAI SDK against the in-process proxy. The key is verify-only (hash stored),
/// and the proxy's <c>ApiKeyResolver</c> resolves it via <see cref="IApiKeyRepository.FindByKeyAsync"/>
/// — the seam asserted here — to the showcase project and the live provider.
/// </summary>
[TestClass]
public sealed class DemoApiKeySeedingTests : BaseTest<Module>
{
    private const string RealApiKey = "sk-real-kiosk-key";
    private const string RealBaseUrl = "https://api.example-llm.com/v1";
    private const string RealModel = "demo-gpt";
    private const string DemoKeyPlaintext = "pk-kiosk-demo";

    private static void RegisterKiosk(ContainerBuilder builder, KioskEndpointOptions endpoint)
    {
        builder.RegisterInstance(new KioskOptions { Enabled = true }).AsSelf();
        builder.RegisterInstance(endpoint).AsSelf();
    }

    private static async Task SeedCoreThenApiKeyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<CoreSeedScenario>().SeedAsync(cancellationToken);
        await services.GetRequiredService<DemoApiKeySeedScenario>().SeedAsync(cancellationToken);
    }

    [TestMethod]
    public async Task SeedAsync_WhenEndpointConfigured_SeedsResolvableIngestionKeyForShowcaseProject()
    {
        IServiceProvider services = GetServices(builder => RegisterKiosk(builder, new KioskEndpointOptions
        {
            BaseUrl = RealBaseUrl,
            ApiKey = RealApiKey,
            Model = RealModel,
        }));

        await SeedCoreThenApiKeyAsync(services, CancellationToken);

        var apiKeys = services.GetRequiredService<IApiKeyRepository>();
        var resolved = await apiKeys.FindByKeyAsync(DemoKeyPlaintext, CancellationToken);

        resolved.Should().NotBeNull("the fixed demo key must be resolvable by its configured plaintext");
        ArgumentNullException.ThrowIfNull(resolved);
        resolved.Project.Name.Should().Be("Showcase Project");
        resolved.Provider.ApiKey.Should().Be(RealApiKey, "the key attaches to the live provider so the proxy forwards upstream");
        resolved.Scopes.HasFlag(ApiKeyScopes.Ingestion).Should().BeTrue("the ingestion proxy requires the Ingestion scope");
    }

    [TestMethod]
    public async Task SeedAsync_StoresKeyHashed_NotPlaintext()
    {
        IServiceProvider services = GetServices(builder => RegisterKiosk(builder, new KioskEndpointOptions
        {
            BaseUrl = RealBaseUrl,
            ApiKey = RealApiKey,
            Model = RealModel,
        }));

        await SeedCoreThenApiKeyAsync(services, CancellationToken);

        var apiKeys = services.GetRequiredService<IApiKeyRepository>();
        var resolved = await apiKeys.FindByKeyAsync(DemoKeyPlaintext, CancellationToken);

        ArgumentNullException.ThrowIfNull(resolved);
        resolved.KeyHash.Should().NotBe(DemoKeyPlaintext, "the key is a verify-only credential stored as a hash");
        resolved.KeyHash.Should().MatchRegex("^[0-9A-Fa-f]{64}$", "the stored value is a hex SHA-256 hash");
    }

    [TestMethod]
    public async Task SeedAsync_WhenNoEndpointConfigured_SeedsNoDemoKey()
    {
        IServiceProvider services = GetServices(builder =>
            RegisterKiosk(builder, new KioskEndpointOptions()));

        await SeedCoreThenApiKeyAsync(services, CancellationToken);

        var apiKeys = services.GetRequiredService<IApiKeyRepository>();
        var resolved = await apiKeys.FindByKeyAsync(DemoKeyPlaintext, CancellationToken);

        resolved.Should().BeNull("without a live endpoint there is no in-process proxy route and no provider to attach a key to");
    }
}
