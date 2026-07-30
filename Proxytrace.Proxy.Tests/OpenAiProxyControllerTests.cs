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

    [TestMethod]
    public async Task Proxy_ChunkedRequestBodyOverCap_Returns413_WithoutBufferingTheWholeBody()
    {
        // Regression: MaxRequestBodyBytes was only checked AFTER Request.Body.CopyToAsync had already
        // grown the MemoryStream to whatever the client sent. A chunked request reports no
        // Content-Length, so the pre-check cannot see its size — the copy itself has to be bounded.
        // (The cap is also reachable at all only now that the proxy host pins Kestrel's own
        // MaxRequestBodySize to the same constant; Kestrel's 30 MB default used to reject first,
        // making both the constant and this 413 dead code.)
        const long overCap = OpenAiProxyController.MaxRequestBodyBytes + (8L * 1024 * 1024);
        var body = new GeneratedByteStream(overCap);

        var controller = BuildController(Substitute.For<IIngestionStream>(), ResolverFor(ApiKey()));
        controller.ControllerContext = BuildContext("Bearer valid", body: "{}");
        // Replace the buffered body with an unbounded one and leave ContentLength unset, exactly as a
        // chunked upload arrives.
        controller.ControllerContext.HttpContext.Request.Body = body;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        body.BytesRead.Should().BeLessThan(
            overCap, "the copy must abort at the cap instead of buffering the entire body first");
        body.BytesRead.Should().BeLessThanOrEqualTo(
            OpenAiProxyController.MaxRequestBodyBytes + (64L * 1024),
            "at most the chunk that crossed the cap may be read before bailing");
    }

    [TestMethod]
    public async Task Proxy_StreamingUpstreamSingleHugeLine_ForwardsVerbatim_InBoundedWrites()
    {
        // Regression: the streaming path read with StreamReader.ReadLineAsync, which grows an
        // unbounded internal buffer until it finds a '\n'. Whether a response is treated as an event
        // stream is decided by the REQUEST ("stream": true), never by the upstream — so a provider
        // that ignores the flag, or a WAF/error page in front of it, can answer with one
        // multi-megabyte single-line body. That was materialized whole as a string and then re-rented
        // at 3x its length to forward. The bytes must still reach the client verbatim, in bounded
        // pieces.
        var line = new string('x', (4 * 1024 * 1024) + 7); // deliberately contains no '\n'
        var bodyBytes = Encoding.UTF8.GetBytes(line);

        var responseBody = new RecordingResponseStream();
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new ChunkedRawHttpClientFactory(bodyBytes, maxBytesPerRead: 64 * 1024));
        controller.ControllerContext = BuildContext(
            "Bearer valid", body: """{"model":"gpt-4o","stream":true,"messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        Encoding.UTF8.GetString(responseBody.Written).Should().Be(
            line + "\n", "the forwarded body must be byte-for-byte upstream's, plus the line terminator");
        responseBody.LargestWriteBytes.Should().BeLessThan(
            1024 * 1024,
            "an un-terminated line must be flushed in bounded segments, never held whole in memory");
    }

    [TestMethod]
    public async Task Proxy_StreamingSseWithCrlf_NormalizesToLf_AndCapturesTranscript()
    {
        // The chunked line splitter replaced ReadLineAsync; it must keep that method's line semantics
        // — CRLF and lone LF both terminate a line, and the forwarded/captured copies use LF.
        const string upstreamBody = "data: {\"a\":1}\r\n\r\ndata: [DONE]\r\n";
        const string expected = "data: {\"a\":1}\n\ndata: [DONE]\n";

        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseBody = new MemoryStream();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            // One byte per read, so every line terminator (and the CR/LF pair itself) is split across
            // chunk boundaries.
            new ChunkedRawHttpClientFactory(Encoding.UTF8.GetBytes(upstreamBody), maxBytesPerRead: 1));
        controller.ControllerContext = BuildContext(
            "Bearer valid", body: """{"model":"gpt-4o","stream":true,"messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        Encoding.UTF8.GetString(responseBody.ToArray()).Should().Be(expected);
        captured.Should().NotBeNull();
        captured?.ResponseBody.Should().Be(expected, "the captured transcript mirrors what was forwarded");
    }

    [TestMethod]
    public async Task Proxy_StreamingSseWithCrOnlyLineEndings_ForwardsEachEventAsItArrives()
    {
        // Regression for #480: the chunk splitter that replaced ReadLineAsync split only on '\n'. SSE
        // also permits a lone '\r' as a line terminator — ReadLineAsync honoured it — so a CR-only
        // event stream was held in `pending` until the 256 KiB flush threshold or EOF and reached the
        // client in one batch instead of event by event, defeating streaming. One byte per read, so
        // every terminator also lands on a chunk boundary.
        const string upstreamBody = "data: {\"a\":1}\r\rdata: [DONE]\r";
        const string expected = "data: {\"a\":1}\n\ndata: [DONE]\n";

        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseBody = new RecordingResponseStream();
        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new ChunkedRawHttpClientFactory(Encoding.UTF8.GetBytes(upstreamBody), maxBytesPerRead: 1));
        controller.ControllerContext = BuildContext(
            "Bearer valid", body: """{"model":"gpt-4o","stream":true,"messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        Encoding.UTF8.GetString(responseBody.Written).Should().Be(
            expected, "a lone CR terminates a line and is normalized to LF, exactly as ReadLine did");
        responseBody.WriteCount.Should().Be(
            3, "each CR-terminated event is forwarded as it arrives, not batched up at EOF");
        captured.Should().NotBeNull();
        captured?.ResponseBody.Should().Be(expected, "the captured transcript mirrors what was forwarded");
    }

    [TestMethod]
    public async Task Proxy_StreamingSseCrlfSplitAcrossChunkBoundary_CountsTheTerminatorOnce()
    {
        // The subtle half of #480: with '\r' now terminating a line, a CRLF whose '\r' ends one read
        // and whose '\n' starts the next must still be ONE terminator. Counting it twice would inject
        // a spurious empty line into every event of a CRLF stream. One byte per read puts the seam
        // between the CR and the LF deterministically.
        const string upstreamBody = "data: a\r\ndata: b\r\n";
        const string expected = "data: a\ndata: b\n";

        var responseBody = new RecordingResponseStream();
        var controller = BuildController(
            Substitute.For<IIngestionStream>(),
            ResolverFor(ApiKey()),
            new ChunkedRawHttpClientFactory(Encoding.UTF8.GetBytes(upstreamBody), maxBytesPerRead: 1));
        controller.ControllerContext = BuildContext(
            "Bearer valid", body: """{"model":"gpt-4o","stream":true,"messages":[]}""");
        controller.ControllerContext.HttpContext.Response.Body = responseBody;

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        Encoding.UTF8.GetString(responseBody.Written).Should().Be(expected);
        responseBody.WriteCount.Should().Be(
            2, "a CRLF split across two reads is one line terminator, not two");
    }

    [TestMethod]
    public async Task Proxy_BufferedUpstreamStallsAfterHeaders_AbortsAtTheClientTimeout_AndRecords504()
    {
        // Regression for #475: the buffered branch reads with ResponseHeadersRead, and
        // HttpClient.Timeout stops applying the moment the headers are in — so an upstream that sends
        // headers and then stalls held the request, a socket and a thread-pool continuation open until
        // the *client* gave up. The copy loop now carries the bound itself, sourced from the same
        // HttpClient timeout; this test proves the wiring by shortening that timeout — a hardcoded
        // five minutes in the controller would hang here instead.
        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new StallingBodyHttpClientFactory(TimeSpan.FromSeconds(1)));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");

        await controller.Proxy("chat/completions", project: null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(
            StatusCodes.Status504GatewayTimeout, "a stalled upstream body is a gateway timeout, not a hang");
        captured.Should().NotBeNull();
        captured?.HttpStatus.Should().Be(
            StatusCodes.Status504GatewayTimeout, "the trace records the timeout rather than upstream's 200");
    }

    [TestMethod]
    public async Task Proxy_BufferedUpstreamStalls_WhenClientDisconnects_PropagatesCancellation_NotATimeout()
    {
        // The other side of #475: the body bound must stay distinguishable from a client abort. With a
        // long upstream budget and the *request* token tripping, the cancellation propagates exactly as
        // it did before (there is nobody left to answer) instead of being reported as a 504.
        IngestMessage? captured = null;
        var stream = Substitute.For<IIngestionStream>();
        stream.PublishAsync(Arg.Do<IngestMessage>(m => captured = m), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Cancelled the moment the proxy asks for the first body byte — a client that disconnects while
        // the proxy waits on a stalled upstream, with the five-minute body budget nowhere near tripping.
        using var clientGoneAway = new CancellationTokenSource();

        var controller = BuildController(
            stream,
            ResolverFor(ApiKey()),
            new StallingBodyHttpClientFactory(TimeSpan.FromMinutes(5), clientGoneAway));
        controller.ControllerContext = BuildContext("Bearer valid", body: """{"model":"gpt-4o","messages":[]}""");

        await FluentActions
            .Awaiting(() => controller.Proxy("chat/completions", project: null, clientGoneAway.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        controller.Response.StatusCode.Should().Be(
            (int)HttpStatusCode.OK, "a client abort must not be rewritten into an upstream timeout");
        captured.Should().NotBeNull();
        captured?.HttpStatus.Should().Be(
            (int)HttpStatusCode.OK, "the partial capture keeps upstream's own status on a client abort");
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
