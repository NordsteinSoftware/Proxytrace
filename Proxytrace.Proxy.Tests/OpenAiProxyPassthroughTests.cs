using System.Net;
using System.Text;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Proxytrace.Domain.ApiKey;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;
using Proxytrace.Proxy.Controllers;

namespace Proxytrace.Proxy.Tests;

/// <summary>
/// The untraced pass-through: any path under <c>/{project}/…</c> that is not the traced
/// <c>openai/v1/…</c> surface (e.g. <c>/{project}/health</c>) is transparently reverse-proxied to the
/// provider's host origin, with the provider's real key swapped in and nothing ingested.
/// </summary>
[TestClass]
public sealed class OpenAiProxyPassthroughTests
{
    [TestMethod]
    public async Task Passthrough_MissingAuthorization_ReturnsUnauthorized()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), NoKeyResolver());
        controller.ControllerContext = BuildContext(authHeader: "");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [TestMethod]
    public async Task Passthrough_BogusKey_ReturnsUnauthorized()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), NoKeyResolver());
        controller.ControllerContext = BuildContext("Bearer not-a-real-key");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [TestMethod]
    public async Task Passthrough_KeyWithoutThePassthroughScope_IsForbiddenAndForwardsNothing()
    {
        // Pass-through needs its own scope, not merely the Ingestion scope that admitted the key to
        // the proxy: it relays any method and any path to the provider ORIGIN with the organisation's
        // real upstream credential attached, and none of it is detected, traced or audited. On a
        // provider serving account-management routes at the same host, that is the provider account's
        // reach, handed to a key issued only to capture traffic.
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"), ApiKeyScopes.Ingestion)),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        capture.LastUri.Should().BeNull("the request must never reach the provider");
    }

    [TestMethod]
    public async Task Passthrough_KeyWithThePassthroughScope_IsForwarded()
    {
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"), ApiKeyScopes.Ingestion | ApiKeyScopes.Passthrough)),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        capture.LastUri.Should().Be(new Uri("http://upstream.test/health"));
    }

    [TestMethod]
    public async Task Passthrough_AuthenticatedWithTheProvidersOwnKey_IsForwardedUngated()
    {
        // A caller holding the provider's own credential can already call the provider directly, so
        // gating which of its paths they may reach through Proxytrace would protect nothing. That
        // path resolves with no scopes at all, which is what null means here.
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"), scopes: null)),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer sk-upstream");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        capture.LastUri.Should().Be(new Uri("http://upstream.test/health"));
    }

    [TestMethod]
    public async Task Passthrough_ValidKey_ForwardsToUpstreamOrigin_NotEndpointPath()
    {
        // Provider endpoint carries the versioned `/v1` path; the upstream /health lives at the host
        // ROOT, sibling of /v1. Pass-through must target the origin, not endpoint + "/health".
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        capture.LastUri.Should().Be(new Uri("http://upstream.test/health"));
        capture.LastMethod.Should().Be(HttpMethod.Get);
    }

    [TestMethod]
    public async Task Passthrough_SwapsInProviderApiKey_NotTheClientBearer()
    {
        var capture = new CapturingHttpMessageHandler("""{"status":"ok"}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer proxytrace-minted-token");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        capture.LastAuthorization.Should().Be("Bearer sk-upstream",
            "the client's (possibly Proxytrace-minted) bearer must be replaced by the provider's real key");
    }

    [TestMethod]
    public async Task Passthrough_PreservesQueryString()
    {
        var capture = new CapturingHttpMessageHandler("""{"object":"list","data":[]}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", query: "?limit=2");

        await controller.Passthrough("acme", "v1/models", CancellationToken.None);

        capture.LastUri.Should().Be(new Uri("http://upstream.test/v1/models?limit=2"));
    }

    [TestMethod]
    public async Task Passthrough_PostBody_ForwardsBytesAndContentType()
    {
        var capture = new CapturingHttpMessageHandler("""{"ok":true}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", method: "POST", body: """{"probe":true}""");
        controller.ControllerContext.HttpContext.Request.ContentType = "application/json";

        await controller.Passthrough("acme", "v1/embeddings", CancellationToken.None);

        capture.LastMethod.Should().Be(HttpMethod.Post);
        Encoding.UTF8.GetString(capture.LastBody).Should().Be("""{"probe":true}""");
        capture.LastContentType.Should().Contain("application/json");
    }

    [TestMethod]
    public async Task Passthrough_UpstreamRedirect_RelaysLocationAndRetryAfter()
    {
        // Response headers are relayed transparently (minus hop-by-hop), so a relayed 3xx/429 keeps
        // its Location and Retry-After and stays actionable for the client.
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(new FakeHttpMessageHandler(
                "",
                HttpStatusCode.Redirect,
                new Dictionary<string, string>
                {
                    ["Location"] = "http://upstream.test/elsewhere",
                    ["Retry-After"] = "17",
                })));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.Redirect);
        controller.Response.Headers.Location.ToString().Should().Be("http://upstream.test/elsewhere");
        controller.Response.Headers.RetryAfter.ToString().Should().Be("17");
    }

    [TestMethod]
    public async Task Passthrough_DoesNotIngest()
    {
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new FakeHttpClientFactory("""{"status":"ok"}"""));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        await stream.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [TestMethod]
    public async Task Passthrough_ReturnsUpstreamStatusAndBody()
    {
        var responseBody = new MemoryStream();
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(new FakeHttpMessageHandler(
                """{"status":"degraded"}""", HttpStatusCode.ServiceUnavailable)));
        controller.ControllerContext = BuildContext("Bearer valid");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
        Encoding.UTF8.GetString(responseBody.ToArray()).Should().Be("""{"status":"degraded"}""");
    }

    // The traversal guard is defence-in-depth; the REAL bound is that the forward target host is the
    // operator-configured provider origin and a hostile path can never redirect it to another host
    // (no cross-host SSRF). These host-spoofing inputs carry no `..` (encoded or otherwise), so they
    // still reach the upstream and must stay on the provider origin. (An encoded `..` is now rejected
    // outright — see Passthrough_EncodedPathTraversal_ReturnsBadRequest.)
    [TestMethod]
    [DataRow("//evil.com/x")]
    [DataRow("@evil.com/x")]
    public async Task Passthrough_HostileRest_StaysOnProviderOrigin(string rest)
    {
        var capture = new CapturingHttpMessageHandler("""{"ok":true}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", rest, CancellationToken.None);

        capture.LastUri.Should().NotBeNull();
        capture.LastUri!.Host.Should().Be("upstream.test",
            "a hostile path must never redirect the forward to a different host");
    }

    [TestMethod]
    public async Task Passthrough_PathTraversal_ReturnsBadRequest()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "../../etc/passwd", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // #304: the traversal guard must not be bypassable with a percent-encoded dot. The catch-all
    // route value is decoded once by routing, so a literal-only "../" scan let `%2e%2e` (and
    // double-encoded `%252e%252e`) slip through; the guard now fully decodes before the check. A
    // rejected request must never reach the upstream.
    [TestMethod]
    [DataRow("%2e%2e/%2e%2e/secret")]
    [DataRow("%252e%252e/secret")]
    [DataRow("foo/%2e%2e/%2e%2e/secret")]
    public async Task Passthrough_EncodedPathTraversal_ReturnsBadRequest(string rest)
    {
        var capture = new CapturingHttpMessageHandler("""{"ok":true}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", rest, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        capture.LastUri.Should().BeNull("a request rejected by the traversal guard must never reach the upstream");
    }

    [TestMethod]
    public async Task Passthrough_OversizedRequest_Returns413()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))));
        controller.ControllerContext = BuildContext("Bearer valid", method: "POST");
        controller.ControllerContext.HttpContext.Request.ContentLength = 64L * 1024 * 1024 + 1;

        await controller.Passthrough("acme", "v1/embeddings", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
    }

    [TestMethod]
    public async Task Passthrough_UpstreamThrows_Returns502()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            new ThrowingHttpClientFactory());
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [TestMethod]
    public async Task Passthrough_KioskEnabled_ReturnsServiceUnavailable()
    {
        var controller = new OpenAiProxyController(
            new FakeHttpClientFactory("{}"),
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("http://upstream.test/v1"))),
            Substitute.For<IRequestBlocker>(),
            Substitute.For<IBudgetBlocker>(),
            new KioskOptions { Enabled = true },
            new KioskEndpointOptions(),
            NullLogger<OpenAiProxyController>.Instance);
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Passthrough("acme", "health", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────

    private static OpenAiProxyController BuildController(
        IIngestionStream stream,
        IApiKeyResolver resolver,
        IHttpClientFactory? httpClientFactory = null)
        => new(
            httpClientFactory ?? new FakeHttpClientFactory("{}"),
            stream,
            resolver,
            Substitute.For<IRequestBlocker>(),
            Substitute.For<IBudgetBlocker>(),
            new KioskOptions(),
            new KioskEndpointOptions(),
            NullLogger<OpenAiProxyController>.Instance);

    private static IApiKeyResolver NoKeyResolver()
    {
        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns((ResolvedApiKey?)null);
        return resolver;
    }

    private static IApiKeyResolver ResolverFor(ResolvedApiKey resolved)
    {
        var resolver = Substitute.For<IApiKeyResolver>();
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resolved);
        return resolver;
    }

    private static ResolvedApiKey ApiKey(Uri endpoint, ApiKeyScopes? scopes)
        => ApiKey(endpoint) with { Scopes = scopes };

    private static ResolvedApiKey ApiKey(Uri endpoint)
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        provider.Name.Returns("test-provider");
        provider.ApiKey.Returns("sk-upstream");
        provider.Endpoint.Returns(endpoint);

        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        return new ResolvedApiKey(project, provider);
    }

    private static ControllerContext BuildContext(
        string authHeader, string method = "GET", string body = "", string query = "")
    {
        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(authHeader))
        {
            httpContext.Request.Headers.Authorization = authHeader;
        }

        if (!string.IsNullOrEmpty(query))
        {
            httpContext.Request.QueryString = new QueryString(query);
        }

        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Method = method;
        httpContext.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = httpContext };
    }
}
