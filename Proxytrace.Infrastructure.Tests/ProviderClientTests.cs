using AwesomeAssertions;
using NSubstitute;
using Nordstein.Core.Testing;
using Proxytrace.Domain.Model;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Infrastructure.Internal;
using System.Net;

namespace Proxytrace.Infrastructure.Tests;

[TestClass]
public sealed class ProviderClientTests : BaseTest<Module>
{
    [TestMethod]
    public async Task GetModels_UnsupportedKind_Throws()
    {
        var provider = StubProvider(ModelProviderKind.Unknown);
        var client = new ProviderClient(provider, Substitute.For<IModelRepository>(), new HttpClient(), Substitute.For<IPricingService>());

        await FluentActions
            .Invoking(() => client.GetModelsAsync(CancellationToken))
            .Should()
            .ThrowAsync<NotSupportedException>();
    }

    [TestMethod]
    public async Task VerifyConnection_UnsupportedKind_ReturnsClassifiedFailure()
    {
        var provider = StubProvider(ModelProviderKind.Unknown);
        var client = new ProviderClient(provider, Substitute.For<IModelRepository>(), new HttpClient(), Substitute.For<IPricingService>());

        var result = await client.VerifyConnectionAsync(CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ProviderConnectionError.UnsupportedKind);
        result.ModelCount.Should().Be(0);
    }

    [TestMethod]
    public async Task VerifyConnection_OpenAiUnauthorized_ReturnsClassifiedFailure()
    {
        var handler = new RoutingHandler(modelsStatus: HttpStatusCode.Unauthorized);
        var client = new ProviderClient(
            StubProvider(ModelProviderKind.OpenAi),
            EchoingModelRepository(),
            new HttpClient(handler),
            Substitute.For<IPricingService>());

        var result = await client.VerifyConnectionAsync(CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ProviderConnectionError.Unauthorized);
        result.ModelCount.Should().Be(0);
    }

    [TestMethod]
    public async Task VerifyConnection_TransportFailure_ReturnsNetworkError()
    {
        var handler = new RoutingHandler(transportException: new HttpRequestException("Network unavailable"));
        var client = new ProviderClient(
            StubAzureProvider(),
            EchoingModelRepository(),
            new HttpClient(handler),
            Substitute.For<IPricingService>());

        var result = await client.VerifyConnectionAsync(CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ProviderConnectionError.NetworkError);
    }

    [TestMethod]
    public async Task VerifyConnection_WhenCallerCancels_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var client = new ProviderClient(
            StubAzureProvider(),
            EchoingModelRepository(),
            new HttpClient(new RoutingHandler()),
            Substitute.For<IPricingService>());

        await FluentActions
            .Invoking(() => client.VerifyConnectionAsync(cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    public async Task VerifyConnection_OpenAiReturnsNoModels_ReturnsSuccessWithZeroCount()
    {
        var handler = new RoutingHandler(modelsJson: """{"data":[]}""");
        var client = new ProviderClient(
            StubProvider(ModelProviderKind.OpenAi),
            EchoingModelRepository(),
            new HttpClient(handler),
            Substitute.For<IPricingService>());

        var result = await client.VerifyConnectionAsync(CancellationToken);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.ModelCount.Should().Be(0);
    }

    [TestMethod]
    public async Task GetModels_Azure_UsesDeployments_PricesByBaseModel_NeverFallsBackToModelsList()
    {
        var handler = new RoutingHandler(
            deploymentsJson: """{"data":[{"id":"my-deploy","model":"gpt-4o"}]}""",
            modelsJson: """{"data":[{"id":"should-not-appear"}]}""");
        var provider = StubAzureProvider();
        var modelRepo = EchoingModelRepository();
        var pricing = Substitute.For<IPricingService>();
        pricing.ResolveAsync(Arg.Any<IModelProvider>(), Arg.Any<DiscoveredModel>(), Arg.Any<CancellationToken>())
            .Returns(ModelPrice.Unknown);
        var client = new ProviderClient(provider, modelRepo, new HttpClient(handler), pricing);

        var result = await client.GetModelsAsync(CancellationToken);

        // The deployment id is the endpoint's model name; "should-not-appear" from /models never surfaces.
        result.Should().ContainSingle();
        result[0].Model.Name.Should().Be("my-deploy");
        // Pricing is resolved against the deployment's base model, not the deployment id.
        await pricing.Received(1).ResolveAsync(
            provider,
            Arg.Is<DiscoveredModel>(d => d != null && d.Name == "my-deploy" && d.PricingModelName == "gpt-4o"),
            Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetModels_AzureUnauthorized_ThrowsClassifiedError_NoModelsFallback()
    {
        var handler = new RoutingHandler(
            deploymentsStatus: HttpStatusCode.Unauthorized,
            modelsJson: """{"data":[{"id":"should-not-appear"}]}""");
        var provider = StubAzureProvider();
        var client = new ProviderClient(provider, EchoingModelRepository(), new HttpClient(handler), Substitute.For<IPricingService>());

        var exception = await FluentActions
            .Invoking(() => client.GetModelsAsync(CancellationToken))
            .Should()
            .ThrowAsync<ProviderConnectionException>();

        exception.Which.Error.Should().Be(ProviderConnectionError.Unauthorized);
        handler.ModelsRequestCount.Should().Be(0);
    }

    [TestMethod]
    public async Task VerifyConnection_AzureUnauthorized_ReturnsClassifiedFailure()
    {
        var handler = new RoutingHandler(deploymentsStatus: HttpStatusCode.Unauthorized);
        var client = new ProviderClient(
            StubAzureProvider(),
            EchoingModelRepository(),
            new HttpClient(handler),
            Substitute.For<IPricingService>());

        var result = await client.VerifyConnectionAsync(CancellationToken);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(ProviderConnectionError.Unauthorized);
        handler.ModelsRequestCount.Should().Be(0);
    }

    private static IModelRepository EchoingModelRepository()
    {
        var repo = Substitute.For<IModelRepository>();
        repo.GetOrCreateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var model = Substitute.For<IModel>();
                model.Name.Returns(ci.Arg<string>());
                return model;
            });
        return repo;
    }

    private static IModelProvider StubProvider(ModelProviderKind kind)
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Name.Returns("Stub");
        provider.Endpoint.Returns(new Uri("https://example.test/v1/"));
        provider.ApiKey.Returns("sk-test");
        provider.Kind.Returns(kind);
        return provider;
    }

    private static IModelProvider StubAzureProvider()
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Endpoint.Returns(new Uri("https://my-resource.openai.azure.com/"));
        provider.ApiKey.Returns("sk-test");
        provider.Kind.Returns(ModelProviderKind.OpenAiCompatible);
        return provider;
    }

    private sealed class RoutingHandler(
        string deploymentsJson = """{"data":[]}""",
        string modelsJson = """{"data":[]}""",
        HttpStatusCode deploymentsStatus = HttpStatusCode.OK,
        HttpStatusCode modelsStatus = HttpStatusCode.OK,
        Exception? transportException = null) : HttpMessageHandler
    {
        public int ModelsRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (transportException is not null)
                return Task.FromException<HttpResponseMessage>(transportException);

            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/deployments"))
            {
                return Task.FromResult(new HttpResponseMessage(deploymentsStatus)
                {
                    Content = new StringContent(deploymentsJson, System.Text.Encoding.UTF8, "application/json"),
                });
            }

            ModelsRequestCount++;
            return Task.FromResult(new HttpResponseMessage(modelsStatus)
            {
                Content = new StringContent(modelsJson, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
