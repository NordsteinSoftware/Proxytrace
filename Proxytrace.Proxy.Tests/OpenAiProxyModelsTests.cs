using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;

namespace Proxytrace.Proxy.Tests;

[TestClass]
public sealed class OpenAiProxyModelsTests
{
    // Azure has no OpenAI-style /models route that lists usable models — its deployments live at
    // /openai/deployments?api-version=…. A blind passthrough therefore returns an empty list. The
    // proxy must translate GET /models to the deployments listing and reshape it to an OpenAI list.
    [TestMethod]
    public async Task Proxy_GetModels_AzureUpstream_TranslatesDeploymentsToOpenAiModelList()
    {
        var handler = new AzureRoutingHandler(
            deploymentsBody: """
                {"object":"list","data":[
                  {"id":"gpt-4o-prod","model":"gpt-4o","object":"deployment"},
                  {"id":"embed-small","model":"text-embedding-3-small","object":"deployment"}
                ]}
                """,
            // What Azure actually returns for the OpenAI /models route: nothing usable.
            modelsBody: """{"object":"list","data":[]}""");

        var controller = BuildController(handler, AzureKey());
        controller.ControllerContext = BuildGetContext("Bearer valid");

        await controller.Proxy("models", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);

        var payload = ReadResponse(controller);
        var ids = ModelIds(payload);
        ids.Should().BeEquivalentTo(new[] { "gpt-4o-prod", "embed-small" });

        ObjectField(payload).Should().Be("list");

        // It must have asked Azure for deployments (with api-version + api-key), not /models.
        handler.DeploymentsRequested.Should().BeTrue();
        handler.LastApiKeyHeader.Should().Be("sk-upstream");
    }

    [TestMethod]
    public async Task Proxy_GetModels_NonAzureUpstream_PassesUpstreamListThroughVerbatim()
    {
        const string upstreamList =
            """{"object":"list","data":[{"id":"gpt-4o","object":"model"}]}""";

        var controller = BuildController(new FixedHandler(upstreamList), OpenAiKey());
        controller.ControllerContext = BuildGetContext("Bearer valid");

        await controller.Proxy("models", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        ReadResponse(controller).Should().Be(upstreamList, "non-Azure /models stays a transparent passthrough");
    }

    [TestMethod]
    public async Task Proxy_GetModels_AzureDeploymentsFail_PassesUpstreamStatusThrough()
    {
        var handler = new AzureRoutingHandler(
            deploymentsBody: """{"error":"boom"}""",
            modelsBody: "{}",
            deploymentsStatus: HttpStatusCode.Unauthorized);

        var controller = BuildController(handler, AzureKey());
        controller.ControllerContext = BuildGetContext("Bearer valid");

        await controller.Proxy("models", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.Unauthorized);
    }

    // Regression: ServeAzureModelsAsync attached the provider key through the *validating* header
    // APIs (HttpRequestHeaders.Add and the Authorization setter). Both throw FormatException on a key
    // carrying a trailing newline or control character — the classic paste artefact — and that
    // escaped the action as an unhandled 500 instead of the upstream status / 502 this path carefully
    // produces. Every other header the proxy sets goes through TryAddWithoutValidation for exactly
    // this reason.
    [TestMethod]
    public async Task Proxy_GetModels_AzureKeyWithTrailingNewline_DoesNotThrowUnhandled()
    {
        var handler = new AzureRoutingHandler(
            deploymentsBody: """{"object":"list","data":[{"id":"gpt-4o-prod","model":"gpt-4o"}]}""",
            modelsBody: """{"object":"list","data":[]}""");

        var controller = BuildController(handler, Key(new Uri("https://myres.openai.azure.com/openai/v1"), "sk-upstream\n"));
        controller.ControllerContext = BuildGetContext("Bearer valid");

        await FluentActions
            .Awaiting(() => controller.Proxy("models", project: null, CancellationToken.None))
            .Should().NotThrowAsync("a malformed provider key must not escape the proxy as an unhandled 500");

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        handler.DeploymentsRequested.Should().BeTrue("the deployments lookup must still be attempted");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static OpenAiProxyController BuildController(HttpMessageHandler handler, ResolvedApiKey key)
        => new(
            new SingleClientFactory(handler),
            Substitute.For<IIngestionStream>(),
            ResolverFor(key),
            Substitute.For<IRequestBlocker>(),
            Substitute.For<IBudgetBlocker>(),
            new KioskOptions(),
            new KioskEndpointOptions(),
            NullLogger<OpenAiProxyController>.Instance);

    private static string ReadResponse(ControllerBase controller)
        => Encoding.UTF8.GetString(((MemoryStream)controller.Response.Body).ToArray());

    private static string[] ModelIds(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString() ?? string.Empty)
            .ToArray();
    }

    private static string? ObjectField(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("object").GetString();
    }

    private static IApiKeyResolver ResolverFor(ResolvedApiKey resolved)
    {
        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resolved);
        return resolver;
    }

    private static ResolvedApiKey AzureKey() => Key(new Uri("https://myres.openai.azure.com/openai/v1"));
    private static ResolvedApiKey OpenAiKey() => Key(new Uri("https://api.openai.com/v1"));

    private static ResolvedApiKey Key(Uri endpoint, string apiKey = "sk-upstream")
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        provider.Name.Returns("test-provider");
        provider.ApiKey.Returns(apiKey);
        provider.Endpoint.Returns(endpoint);

        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        return new ResolvedApiKey(project, provider);
    }

    private static ControllerContext BuildGetContext(string authHeader)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = authHeader;
        httpContext.Request.Method = "GET";
        httpContext.Request.Body = new MemoryStream([]);
        httpContext.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = httpContext };
    }

    private sealed class AzureRoutingHandler : HttpMessageHandler
    {
        private readonly string deploymentsBody;
        private readonly string modelsBody;
        private readonly HttpStatusCode deploymentsStatus;

        public AzureRoutingHandler(
            string deploymentsBody,
            string modelsBody,
            HttpStatusCode deploymentsStatus = HttpStatusCode.OK)
        {
            this.deploymentsBody = deploymentsBody;
            this.modelsBody = modelsBody;
            this.deploymentsStatus = deploymentsStatus;
        }

        public bool DeploymentsRequested { get; private set; }
        public string? LastApiKeyHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isDeployments = request.RequestUri?.AbsolutePath.EndsWith("/openai/deployments") == true;
            if (isDeployments)
            {
                DeploymentsRequested = true;
                LastApiKeyHeader = request.Headers.TryGetValues("api-key", out var v) ? v.FirstOrDefault() : null;
            }

            var (status, body) = isDeployments ? (deploymentsStatus, deploymentsBody) : (HttpStatusCode.OK, modelsBody);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FixedHandler : HttpMessageHandler
    {
        private readonly string body;
        public FixedHandler(string body) => this.body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler handler;
        public SingleClientFactory(HttpMessageHandler handler) => this.handler = handler;
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
