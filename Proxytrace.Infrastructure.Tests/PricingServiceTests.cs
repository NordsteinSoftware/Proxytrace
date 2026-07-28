using System.Net;
using System.Text;
using AwesomeAssertions;
using NSubstitute;
using Proxytrace.Common.Async;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Infrastructure.Internal;

namespace Proxytrace.Infrastructure.Tests;

[TestClass]
public sealed class PricingServiceTests
{
    public required TestContext TestContext { get; init; }

    private const string Catalog =
        """
        {
          "gpt-4o": { "input_cost_per_token": 0.000001, "output_cost_per_token": 0.000002 },
          "azure/gpt-4o": { "input_cost_per_token": 0.000003, "output_cost_per_token": 0.000002 }
        }
        """;

    [TestMethod]
    public async Task Resolve_AzureProvider_PrefersAzureCatalogEntry()
    {
        var sut = BuildService(fx: 1.0m);

        var price = await sut.ResolveAsync(
            StubProvider("https://x.openai.azure.com/"),
            new DiscoveredModel("my-deploy", "gpt-4o"),
            TestContext.CancellationToken);

        // azure/gpt-4o (0.000003 × 1e6 × 1.0) wins over bare gpt-4o.
        price.InputTokenCost.Should().Be(3.0m);
    }

    [TestMethod]
    public async Task Resolve_NonAzureProvider_UsesBareModelName()
    {
        var sut = BuildService(fx: 0.5m);

        var price = await sut.ResolveAsync(
            StubProvider("https://api.openai.com/v1"),
            new DiscoveredModel("gpt-4o", "gpt-4o"),
            TestContext.CancellationToken);

        // bare gpt-4o (0.000001 × 1e6 × 0.5).
        price.InputTokenCost.Should().Be(0.5m);
    }

    [TestMethod]
    public async Task Resolve_AzureProvider_FallsBackToBareName_WhenNoAzureEntry()
    {
        const string bareOnly = """{"gpt-4o":{"input_cost_per_token":0.000001,"output_cost_per_token":0.000002}}""";
        var sut = BuildService(fx: 1.0m, catalog: bareOnly);

        var price = await sut.ResolveAsync(
            StubProvider("https://x.openai.azure.com/"),
            new DiscoveredModel("my-deploy", "gpt-4o"),
            TestContext.CancellationToken);

        price.InputTokenCost.Should().Be(1.0m);
    }

    private static IModelProvider StubProvider(string endpoint)
    {
        var p = Substitute.For<IModelProvider>();
        p.Endpoint.Returns(new Uri(endpoint));
        return p;
    }

    private static PricingService BuildService(decimal fx, string catalog = Catalog)
    {
        var fxProvider = Substitute.For<IFxRateProvider>();
        fxProvider.GetUsdToEurAsync(Arg.Any<CancellationToken>()).Returns(fx);
        var liteLlm = new LiteLlmCatalogResolver(new HttpClient(new StubHandler(catalog)), new PricingOptions(), fxProvider, Substitute.For<IAsyncLock>(), new MutableClock());
        return new PricingService(liteLlm);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
