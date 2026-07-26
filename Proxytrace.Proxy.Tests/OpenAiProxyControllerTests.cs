using System.Net;
using System.Text;
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
public sealed class OpenAiProxyControllerTests
{
    [TestMethod]
    public async Task Proxy_MissingAuthorization_ReturnsUnauthorized()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), NoKeyResolver());
        controller.ControllerContext = BuildContext(authHeader: "");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [TestMethod]
    public async Task Proxy_BogusKey_ReturnsUnauthorized()
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), NoKeyResolver());
        controller.ControllerContext = BuildContext("Bearer not-a-real-key");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    // #304: the shared traversal guard protects the traced action too. A literal or percent-encoded
    // (single- or double-encoded) `..` must be rejected with a 400 before any upstream contact.
    [TestMethod]
    [DataRow("../secret")]
    [DataRow("%2e%2e/secret")]
    [DataRow("%252e%252e/secret")]
    public async Task Proxy_PathTraversal_ReturnsBadRequest(string path)
    {
        var controller = BuildController(Substitute.For<IIngestionStream>(), ResolverFor(ApiKey()));
        controller.ControllerContext = BuildContext("Bearer valid");

        await controller.Proxy(path, project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [TestMethod]
    public async Task Proxy_ValidKey_ForwardsUpstream_AndPublishesIngestion()
    {
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("hello")));
        controller.ControllerContext = BuildContext(
            "Bearer valid",
            body: """{"model":"gpt-4o","messages":[{"role":"user","content":"hi"}]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        await stream.Received(1).PublishAsync(Arg.Any<IngestMessage>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Proxy_UpstreamThrows_Returns502()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new ThrowingHttpClientFactory());
        controller.ControllerContext = BuildContext("Bearer valid", body: "{}");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [TestMethod]
    public async Task Proxy_PublishThrows_DoesNotBreakResponse()
    {
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Any<IngestMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("redis down")));

        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("ok")));
        controller.ControllerContext = BuildContext("Bearer valid", body: "{}");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task Proxy_GetWithoutBody_DoesNotForwardARequestBody()
    {
        var capture = new CapturingHttpMessageHandler("""{"object":"list","data":[]}""");
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: "", method: "GET");

        await controller.Proxy("models", project: null, CancellationToken.None);

        capture.LastMethod.Should().Be(HttpMethod.Get);
        capture.LastHadContent.Should().BeFalse("a bodyless GET must not be forwarded with a request body");
    }

    [TestMethod]
    public async Task Proxy_PostWithBody_ForwardsBodyUpstream()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHadContent.Should().BeTrue();
        Encoding.UTF8.GetString(capture.LastBody).Should().Be("""{"model":"gpt-4o","messages":[]}""");
    }

    [TestMethod]
    public async Task Proxy_MalformedContentType_DoesNotCrash_AndForwardsHeader()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext(
            "Bearer valid",
            body: """{"model":"gpt-4o","messages":[]}""",
            contentType: "garbage;;");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        capture.LastContentType.Should().Be("garbage;;", "an unparseable Content-Type is forwarded raw, not dropped or fatal");
    }

    // The proxy is a transparent swap-in for the upstream: any header the client sends that is not
    // Proxytrace-specific, a credential, or hop-by-hop must reach the provider unchanged.
    [TestMethod]
    public async Task Proxy_ArbitraryClientHeaders_AreForwardedUpstream()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Request.Headers["OpenAI-Beta"] = "assistants=v2";
        controller.ControllerContext.HttpContext.Request.Headers["openai-organization"] = "org-123";
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "idem-42";
        controller.ControllerContext.HttpContext.Request.Headers["x-custom-trace"] = "abc";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHeaders.Should().Contain("openai-beta", "assistants=v2");
        capture.LastHeaders.Should().Contain("openai-organization", "org-123");
        capture.LastHeaders.Should().Contain("idempotency-key", "idem-42");
        capture.LastHeaders.Should().Contain("x-custom-trace", "abc");
    }

    [TestMethod]
    public async Task Proxy_ProxytraceControlHeaders_AreNotForwardedUpstream()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Request.Headers["x-proxytrace-agent"] = "billing agent";
        controller.ControllerContext.HttpContext.Request.Headers["x-proxytrace-session-id"] = "sess-1";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHeaders.Keys.Should().NotContain(
            key => key.StartsWith("x-proxytrace-"),
            "Proxytrace's own control headers must never leak to the provider");
    }

    [TestMethod]
    public async Task Proxy_HopByHopAndConnectionHeaders_AreNotForwardedUpstream()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        var headers = controller.ControllerContext.HttpContext.Request.Headers;
        headers["Host"] = "proxytrace.example";
        headers["Accept-Encoding"] = "gzip";
        headers["Connection"] = "keep-alive, x-hop-extension";
        headers["x-hop-extension"] = "per-connection";
        headers["Transfer-Encoding"] = "chunked";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHeaders.Keys.Should().NotContain("host", "the upstream host is set by the forward URI");
        capture.LastHeaders.Keys.Should().NotContain("accept-encoding", "the capture pipeline needs an uncompressed body");
        capture.LastHeaders.Keys.Should().NotContain("connection");
        capture.LastHeaders.Keys.Should().NotContain("transfer-encoding");
        capture.LastHeaders.Keys.Should().NotContain("x-hop-extension",
            "headers named by Connection are per-hop (RFC 9110 §7.6.1) and must not travel upstream");
    }

    [TestMethod]
    public async Task Proxy_ClientApiKeyHeader_IsNotForwarded_AndAzureGetsProviderApiKey()
    {
        // Azure's classic data-plane auth reads `api-key`; the client's value (their Proxytrace key)
        // must be replaced by the provider's real key, exactly like the Authorization bearer.
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey(new Uri("https://my-resource.openai.azure.com/openai/v1"))),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Request.Headers["api-key"] = "proxytrace-minted-token";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHeaders.Should().Contain("api-key", "sk-upstream");
        capture.LastAuthorization.Should().Be("Bearer sk-upstream");
    }

    [TestMethod]
    public async Task Proxy_NonAzureUpstream_DoesNotGetApiKeyHeader()
    {
        var capture = new CapturingHttpMessageHandler(FakeHttpMessageHandler.BuildOpenAiResponse("ok"));
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(capture));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Request.Headers["api-key"] = "proxytrace-minted-token";

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        capture.LastHeaders.Keys.Should().NotContain("api-key",
            "the client's api-key may carry their Proxytrace key and must never leak upstream");
    }

    [TestMethod]
    public async Task Proxy_ArbitraryUpstreamResponseHeaders_AreRelayedToClient()
    {
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(new FakeHttpMessageHandler(
                FakeHttpMessageHandler.BuildOpenAiResponse("ok"),
                HttpStatusCode.OK,
                new Dictionary<string, string>
                {
                    ["x-request-id"] = "req-1",
                    ["x-upstream-custom"] = "value",
                    ["Connection"] = "keep-alive",
                })));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.Headers["x-request-id"].ToString().Should().Be("req-1");
        controller.Response.Headers["x-upstream-custom"].ToString().Should().Be("value");
        controller.Response.Headers.Keys.Should().NotContain(
            k => k.Equals("Connection", StringComparison.OrdinalIgnoreCase),
            "hop-by-hop response headers are owned by each connection, not relayed");
    }

    [TestMethod]
    public async Task Proxy_BufferedCapture_PublishesWithIndependentToken_NotRequestToken()
    {
        // The upstream call has completed by the time we publish; a client cancel/disconnect must
        // not drop the captured call, so the publish runs with CancellationToken.None.
        var stream = Substitute.For<IIngestionStream>();
        using var cts = new CancellationTokenSource();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new FakeHttpClientFactory(FakeHttpMessageHandler.BuildOpenAiResponse("hello")));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");

        await controller.Proxy("chat/completions", project: null, cts.Token);

        await stream.Received(1).PublishAsync(Arg.Any<IngestMessage>(), CancellationToken.None);
    }

    [TestMethod]
    public async Task Proxy_BufferedResponseStreamedInChunks_ForwardsFullBody_AndCapturesIt()
    {
        // The buffered path now streams the upstream body through in chunks instead of reading it
        // whole. Forwarding and capture must survive crossing many read boundaries with nothing lost
        // or duplicated. Serve a body well over a single read, dripped in small chunks.
        var body = "{\"data\":\"" + new string('a', 200 * 1024) + "\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseBody = new MemoryStream();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new ChunkedRawHttpClientFactory(bodyBytes, maxBytesPerRead: 4096));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        responseBody.ToArray().Should().Equal(bodyBytes, "the forwarded body must be byte-for-byte identical across all chunks");
        captured.Should().NotBeNull();
        captured?.ResponseBody.Should().Be(body, "an under-cap response is captured in full");
    }

    [TestMethod]
    public async Task Proxy_BufferedOversizedResponse_ForwardsFullBody_ButBoundsCapturedCopy()
    {
        // Regression for #185: the non-streaming path used to ReadAsStringAsync the entire upstream
        // body unbounded (plus a second copy when re-encoding) and capture it verbatim — an OOM
        // vector on a large/hostile reply. The forwarded bytes must still go through untruncated, but
        // the captured copy must now be bounded the same way the streaming path bounds it. This
        // mirrors the private MaxCapturedResponseChars constant (16 MiB).
        const int capChars = 16 * 1024 * 1024;
        var oversized = new string('x', capChars + 4096);

        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseBody = new MemoryStream();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new FakeHttpClientFactory(oversized));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be((int)HttpStatusCode.OK);
        responseBody.Length.Should().Be(oversized.Length, "the forwarded response body must never be truncated");
        captured.Should().NotBeNull();
        captured?.ResponseBody.Should().HaveLength(capChars, "the captured copy must be bounded at MaxCapturedResponseChars");
    }

    [TestMethod]
    public async Task Proxy_BufferedResponseMultiByteCharSplitAcrossChunks_CapturedWithoutCorruption()
    {
        // Drip the body one byte per read so every multi-byte UTF-8 character (€ = 3 bytes, é = 2) is
        // split across reads. A naive per-chunk decode would emit replacement chars at the seams; the
        // Decoder must reassemble them. Forwarded bytes stay exact and the captured text round-trips.
        var body = "{\"text\":\"café costs 5€ — déjà vu\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseBody = new MemoryStream();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new ChunkedRawHttpClientFactory(bodyBytes, maxBytesPerRead: 1));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        responseBody.ToArray().Should().Equal(bodyBytes, "the forwarded bytes must be untouched regardless of chunking");
        captured.Should().NotBeNull();
        captured?.ResponseBody.Should().Be(body, "multi-byte characters split across chunk reads must be captured intact");
    }

    [TestMethod]
    public async Task Proxy_StreamingClientDisconnect_StillPublishesAccumulatedTranscript()
    {
        var stream = Substitute.For<IIngestionStream>();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new SingleHandlerClientFactory(new CapturingHttpMessageHandler("data: {\"choices\":[]}\n\ndata: [DONE]\n")));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","stream":true,"messages":[]}""");
        // Simulate the client going away mid-stream: writing the forwarded line fails.
        controller.ControllerContext.HttpContext.Response.Body = new ThrowOnWriteStream();

        await FluentActions
            .Awaiting(() => controller.Proxy("chat/completions", project: null, CancellationToken.None))
            .Should().ThrowAsync<IOException>();

        // The accumulated transcript is published despite the client disconnect.
        await stream.Received(1).PublishAsync(Arg.Any<IngestMessage>(), CancellationToken.None);
    }

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

    private static ResolvedApiKey ApiKey(Uri? endpoint = null)
    {
        var provider = Substitute.For<IModelProvider>();
        provider.Id.Returns(Guid.NewGuid());
        provider.Name.Returns("test-provider");
        provider.ApiKey.Returns("sk-upstream");
        provider.Endpoint.Returns(endpoint ?? new Uri("http://upstream.test/"));

        var project = Substitute.For<IProject>();
        project.Id.Returns(Guid.NewGuid());

        return new ResolvedApiKey(project, provider);
    }

    private static ControllerContext BuildContext(
        string authHeader, string body = "{}", string method = "POST", string contentType = "application/json")
    {
        var httpContext = new DefaultHttpContext();
        if (!string.IsNullOrEmpty(authHeader))
        {
            httpContext.Request.Headers.Authorization = authHeader;
        }

        if (!string.IsNullOrEmpty(body))
        {
            httpContext.Request.ContentType = contentType;
        }

        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.Method = method;
        httpContext.Response.Body = new MemoryStream();
        return new ControllerContext { HttpContext = httpContext };
    }
}
