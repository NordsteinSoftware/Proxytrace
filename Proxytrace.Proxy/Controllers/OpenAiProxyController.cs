using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.Kiosk;
using Proxytrace.Domain.ModelProvider;
using Proxytrace.Domain.Project;
using Proxytrace.Messaging;

namespace Proxytrace.Proxy.Controllers;

/// <summary>
/// Transparent reverse proxy for OpenAI-compatible APIs, hosted in the standalone ingestion proxy
/// service. Authenticates the Proxytrace API key, forwards every call upstream, streams the
/// response back to the client, then publishes the captured call to the ingestion stream for the
/// main app to persist. Stays up independently of the rest of the backend.
/// </summary>
[ApiController]
public class OpenAiProxyController : ControllerBase
{
    private const string SessionIdHeader = "x-proxytrace-session-id";

    // Optional: thread-level grouping. Historically x-proxytrace-session-id carried this meaning;
    // it now names the debugging session (a higher-level grouping), and ingestion falls back to it
    // for conversation grouping when this header is absent.
    private const string ConversationIdHeader = "x-proxytrace-conversation-id";

    // Hard caps so a single request can't exhaust proxy memory: reject oversized request bodies
    // outright, and bound the in-memory transcript we accumulate for capture (the bytes are still
    // streamed through to the client untruncated — only the captured copy is bounded).
    //
    // MaxRequestBodyBytes is public because the host has to pin Kestrel's own
    // KestrelServerLimits.MaxRequestBodySize to the *same* number: Kestrel's 30 MB default rejects
    // first otherwise, and this cap — plus the 413 it produces — is unreachable dead code.
    // See Proxytrace.Proxy.Api/Program.cs.
    public const long MaxRequestBodyBytes = 64L * 1024 * 1024;

    private const int MaxCapturedResponseChars = 16 * 1024 * 1024;

    // Chunk size for every stream copy the proxy performs (request buffering, buffered response
    // forwarding, streaming reads).
    private const int CopyChunkBytes = 64 * 1024;
    private const int StreamReadChunkChars = 16 * 1024;

    // Upper bound on how much of a single un-terminated SSE line the proxy holds in memory before
    // flushing it onward. Well above any real `data: …` frame, small enough that a pathological
    // single-line upstream body cannot grow the proxy's working set with it.
    private const int MaxForwardedLineChars = 256 * 1024;

    // Optional: a client may name its owning agent explicitly. When present, ingestion attributes the
    // call to that named agent directly, skipping the prompt/tool similarity matcher.
    private const string AgentNameHeader = "x-proxytrace-agent";

    // Headers owned by Proxytrace itself, prefixed so the strip rule below catches future additions
    // (x-proxytrace-session-id, x-proxytrace-agent, …) — they steer ingestion and never travel
    // upstream.
    private const string ProxytraceHeaderPrefix = "x-proxytrace-";

    // The proxy forwards the request transparently: every client header travels upstream EXCEPT the
    // ones below, so an existing client can swap its base URL to Proxytrace without losing headers
    // its provider needs (OpenAI-Beta, openai-organization, idempotency keys, custom tracing, …).
    // Stripped are only: credentials (replaced with the provider's real key), hop-by-hop headers
    // (owned per-connection, RFC 9110 §7.6.1), and framing/negotiation headers the proxy's own
    // upstream connection must control — Content-Length is recomputed from the buffered (possibly
    // rewritten) body, and Accept-Encoding stays off because the capture pipeline reads the upstream
    // body as plain UTF-8 text and must never receive a compressed stream.
    private static readonly IReadOnlyCollection<string> StrippedRequestHeaders = new HashSet<string>(
    [
        // credentials — replaced with the provider's real key
        "authorization",
        "api-key",
        // hop-by-hop
        "connection",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailer",
        "transfer-encoding",
        "upgrade",
        // owned by the proxy's own upstream connection
        "host",
        "content-length",
        "expect",
        "accept-encoding"
    ]);

    // Response side of the same transparency rule: every upstream header is relayed to the client
    // EXCEPT hop-by-hop headers and the framing headers Kestrel must own. Content-Length is dropped
    // because the streaming path normalizes CRLF line endings to LF, so the relayed byte count can
    // legitimately differ from upstream's.
    private static readonly IReadOnlyCollection<string> StrippedResponseHeaders = new HashSet<string>(
    [
        "connection",
        "keep-alive",
        "proxy-authenticate",
        "proxy-authorization",
        "te",
        "trailer",
        "transfer-encoding",
        "upgrade",
        "content-length"
    ]);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly IIngestionStream stream;
    private readonly IApiKeyResolver apiKeyResolver;
    private readonly IRequestBlocker requestBlocker;
    private readonly KioskOptions kioskOptions;
    private readonly KioskEndpointOptions kioskEndpoint;
    private readonly ILogger<OpenAiProxyController> logger;

    public OpenAiProxyController(
        IHttpClientFactory httpClientFactory,
        IIngestionStream stream,
        IApiKeyResolver apiKeyResolver,
        IRequestBlocker requestBlocker,
        KioskOptions kioskOptions,
        KioskEndpointOptions kioskEndpoint,
        ILogger<OpenAiProxyController> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.stream = stream;
        this.apiKeyResolver = apiKeyResolver;
        this.requestBlocker = requestBlocker;
        this.kioskOptions = kioskOptions;
        this.kioskEndpoint = kioskEndpoint;
        this.logger = logger;
    }

    // Two shapes are accepted: the project-scoped `/{project}/openai/v1/…` form (required for the
    // upstream-provider-key auth path) and the legacy `/openai/v1/…` form (project derived from a
    // Proxytrace-issued key). The literal `openai/v1/…` template is matched ahead of the
    // parameterised one, so `project` is only bound for the scoped form.
    [Route("openai/v1/{**path}")]
    [Route("{project}/openai/v1/{**path}")]
    [HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch, HttpHead, HttpOptions]
    public async Task Proxy(string? path, string? project, CancellationToken cancellationToken)
    {
        // {**path} also matches zero segments (`GET /openai/v1`), in which case the route value is
        // absent and the parameter binds null.
        path ??= string.Empty;

        BufferedProxyRequest? buffered = await GuardAndBufferRequestAsync(path, project, cancellationToken);
        if (buffered is null)
        {
            return;
        }

        ResolvedApiKey resolved = buffered.Resolved;
        var requestBodyBytes = buffered.RequestBodyBytes;

        // Azure has no OpenAI-style /models route that lists usable models — its deployments live at
        // /openai/deployments?api-version=…, so a blind passthrough returns an empty list. Translate
        // GET /models to that deployments listing and reshape it to an OpenAI model list.
        if (HttpMethods.IsGet(Request.Method)
            && IsModelsPath(path)
            && ProviderEndpoints.IsAzure(resolved.Provider.Endpoint))
        {
            await ServeAzureModelsAsync(resolved.Provider, cancellationToken);
            return;
        }

        var requestBody = Encoding.UTF8.GetString(requestBodyBytes);

        var sessionId = Request.Headers.TryGetValue(SessionIdHeader, out var sid)
            ? sid.ToString()
            : null;

        var conversationId = Request.Headers.TryGetValue(ConversationIdHeader, out var cid)
            ? cid.ToString()
            : null;

        var agentName = Request.Headers.TryGetValue(AgentNameHeader, out var an)
            ? an.ToString()
            : null;

        // Real-time blocking detectors: evaluate the raw request body before any upstream contact.
        // On a match the call is rejected (never forwarded) but still recorded as a blocked trace.
        var blockSw = Stopwatch.StartNew();
        BlockedRequestMatch? blocked = await requestBlocker.EvaluateAsync(
            resolved.Project.Id, agentName, requestBody, cancellationToken);
        if (blocked is not null)
        {
            await RejectBlockedRequestAsync(
                resolved, requestBody, blocked, blockSw.Elapsed, sessionId, conversationId, agentName, cancellationToken);
            return;
        }

        var isStreaming = IsStreamingRequest(requestBody, Request.ContentType);

        if (isStreaming)
        {
            requestBodyBytes = InjectStreamUsageOption(requestBodyBytes);
            requestBody = Encoding.UTF8.GetString(requestBodyBytes);
        }

        using var upstream = BuildUpstreamRequest(path, requestBodyBytes, resolved.Provider.ApiKey, resolved.Provider.Endpoint);
        var client = httpClientFactory.CreateClient("openai");
        var sw = Stopwatch.StartNew();

        HttpResponseMessage upstreamResponse;
        try
        {
            // ResponseHeadersRead on BOTH branches. ResponseContentRead would call
            // LoadIntoBufferAsync before returning, materializing the entire upstream body in memory
            // (bounded only by HttpClient.MaxResponseContentBufferSize, ~2 GB by default) — which is
            // exactly the unbounded residency ProxyBufferedResponseAsync's chunked copy exists to
            // avoid. Both paths read the body as a stream, so neither needs the eager buffer.
            upstreamResponse = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upstream request to /v1/{Path} failed", path);
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        // Dispose the response (and its underlying connection, held open by ResponseHeadersRead on
        // the streaming path) once the body has been fully copied/captured.
        using (upstreamResponse)
        {
            CopyUpstreamStatusAndHeaders(upstreamResponse);

            if (isStreaming)
            {
                await ProxyStreamingResponseAsync(resolved.Provider, resolved.Project, requestBody, upstreamResponse, sw, sessionId, conversationId, agentName, cancellationToken);
            }
            else
            {
                await ProxyBufferedResponseAsync(resolved.Provider, resolved.Project, requestBody, upstreamResponse, sw, sessionId, conversationId, agentName, cancellationToken);
            }
        }
    }

    // ── Non-LLM pass-through ────────────────────────────────────────────────────

    // Transparent reverse proxy for every OTHER path under `/{project}/…` — anything that is not the
    // traced `openai/v1/…` surface. A client whose base URL is `/{project}` can reach the non-LLM
    // endpoints the upstream exposes (e.g. `/health`, `/v1/models`) through the same base URL. These
    // calls are forwarded to the provider's host ORIGIN and are deliberately NOT ingested/traced.
    // The literal `{project}/openai/v1/…` route out-ranks this all-parameter catch-all, so LLM calls
    // never reach here.
    [Route("{project}/{**rest}")]
    [HttpGet, HttpPost, HttpPut, HttpDelete, HttpPatch, HttpHead, HttpOptions]
    public async Task Passthrough(string project, string? rest, CancellationToken cancellationToken)
    {
        var path = rest ?? string.Empty;

        BufferedProxyRequest? buffered = await GuardAndBufferRequestAsync(path, project, cancellationToken);
        if (buffered is null)
        {
            return;
        }

        // Map to the provider's host ORIGIN (scheme+host+port), not its versioned API endpoint:
        // `openai/v1/…` is a Proxytrace routing prefix, so the upstream's `/health` etc. live at the
        // host root, siblings of the endpoint's `/v1` path.
        var origin = new Uri(buffered.Resolved.Provider.Endpoint.GetLeftPart(UriPartial.Authority));
        using var upstream = BuildUpstreamRequest(path, buffered.RequestBodyBytes, buffered.Resolved.Provider.ApiKey, origin);

        // The dedicated "passthrough" client does NOT auto-follow redirects: a transparent proxy
        // relays the 3xx (with its Location) to the client instead of chasing it server-side, where
        // the BCL would also strip the Authorization header on the hop.
        var client = httpClientFactory.CreateClient("passthrough");

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await client.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Upstream pass-through to /{Path} failed", path);
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstreamResponse)
        {
            await ForwardResponseAsync(upstreamResponse, cancellationToken);
        }
    }

    // Copy an upstream response straight back to the client — status, headers, and the body
    // byte-for-byte — without capturing or ingesting anything. A raw byte pump with a per-chunk
    // flush transparently handles both plain and event-stream bodies; pass-through is not an OpenAI
    // request, so it skips the streaming detection / stream_options injection the traced path does.
    private async Task ForwardResponseAsync(HttpResponseMessage upstreamResponse, CancellationToken cancellationToken)
    {
        CopyUpstreamStatusAndHeaders(upstreamResponse);

        var buffer = ArrayPool<byte>.Shared.Rent(CopyChunkBytes);
        try
        {
            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);

            int read;
            while ((read = await upstreamStream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ── Shared guard rails ──────────────────────────────────────────────────────

    /// <summary>
    /// The request-side result both proxy actions need: the fully buffered request body and the
    /// resolved API key.
    /// </summary>
    private sealed record BufferedProxyRequest(byte[] RequestBodyBytes, ResolvedApiKey Resolved);

    // Shared prologue for both proxy actions — kiosk refusal, path-traversal guard, request-size
    // caps, API-key resolution, and request-body buffering. Returns null when the request was
    // already terminated with a response status code.
    private async Task<BufferedProxyRequest?> GuardAndBufferRequestAsync(
        string path,
        string? project,
        CancellationToken cancellationToken)
    {
        // Refuse only when kiosk mode has NO live endpoint: a plain demo has no real upstream to
        // forward to. When a live Kiosk:Endpoint IS configured the kiosk API mounts this route
        // in-process so a sample client can turn calls into live traces, so the proxy must serve.
        // Outside kiosk (the standalone proxy host / production) Enabled is false and it always serves.
        if (kioskOptions.Enabled && !kioskEndpoint.IsConfigured)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            Response.ContentType = "application/json";
            await Response.WriteAsync(
                "{\"kiosk\":true,\"message\":\"Proxy disabled in demo mode.\"}",
                cancellationToken);
            return null;
        }

        // Reject path traversal: a catch-all route value could otherwise contain "../" and reach
        // arbitrary paths on the upstream host, escaping the intended forward target. Routing decodes
        // the route value only once, so a naive literal "../" scan is bypassable with a
        // percent-encoded dot (`%2e%2e`, or double-encoded `%252e%252e`); fully decode before the
        // check so no encoding layer can smuggle a `..` past it. The forward host is already pinned to
        // the provider origin (no cross-host SSRF), so this is defense-in-depth against a future
        // change that would rely on the guard actually holding.
        if (ContainsPathTraversal(path))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return null;
        }

        if (Request.ContentLength is > MaxRequestBodyBytes)
        {
            Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        ResolvedApiKey? resolved = await GetResolvedKeyAsync(project, cancellationToken);
        if (resolved is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return null;
        }

        byte[]? requestBodyBytes = await BufferRequestBodyAsync(cancellationToken);
        if (requestBodyBytes is null)
        {
            Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        return new BufferedProxyRequest(requestBodyBytes, resolved);
    }

    // Buffers the request body with the cap enforced *during* the copy, not after it. A chunked
    // request reports no Content-Length up front, so the pre-check above cannot see its size — and a
    // plain CopyToAsync would grow the MemoryStream to whatever the client sends before anyone looked
    // at the total. Reading in fixed-size chunks and bailing the moment the cap is crossed means the
    // resident bytes never exceed MaxRequestBodyBytes. Returns null when the body is over the cap.
    private async Task<byte[]?> BufferRequestBodyAsync(CancellationToken cancellationToken)
    {
        using var buffered = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(CopyChunkBytes);
        try
        {
            int read;
            while ((read = await Request.Body.ReadAsync(chunk.AsMemory(0, CopyChunkBytes), cancellationToken)) > 0)
            {
                if (buffered.Length + read > MaxRequestBodyBytes)
                {
                    return null;
                }

                await buffered.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        return buffered.ToArray();
    }

    // Reports whether the (already once-decoded) catch-all route value hides a `..` traversal
    // segment behind percent-encoding. Fully unescapes the value — iteratively, so a double-encoded
    // `%252e%252e` is unwrapped too — and checks each decoded layer for a literal `..`. A value that
    // still keeps changing after a sane number of rounds is pathological and treated as hostile
    // rather than forwarded.
    private static bool ContainsPathTraversal(string path)
    {
        var current = path;
        for (var depth = 0; depth < 5; depth++)
        {
            if (current.Contains("..", StringComparison.Ordinal))
            {
                return true;
            }

            var decoded = Uri.UnescapeDataString(current);
            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                return false;
            }

            current = decoded;
        }

        return true;
    }

    // Copy upstream status + response headers to our response, minus the stripped set (hop-by-hop
    // and framing headers Kestrel owns). Multi-valued headers are relayed as separate values, not
    // joined — a joined Set-Cookie would corrupt the cookies.
    private void CopyUpstreamStatusAndHeaders(HttpResponseMessage upstreamResponse)
    {
        Response.StatusCode = (int)upstreamResponse.StatusCode;
        foreach (var header in upstreamResponse.Headers.Concat(upstreamResponse.Content.Headers))
        {
            if (!StrippedResponseHeaders.Contains(header.Key.ToLowerInvariant()))
            {
                Response.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }

    // ── Azure /models translation ───────────────────────────────────────────────

    private static bool IsModelsPath(string path) =>
        path.Trim('/').Equals("models", StringComparison.OrdinalIgnoreCase);

    private async Task ServeAzureModelsAsync(IModelProvider provider, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Get, ProviderEndpoints.AzureDeploymentsUri(provider.Endpoint));
        // TryAddWithoutValidation, exactly as BuildUpstreamRequest does: `Headers.Add` and the
        // Authorization setter both validate, and a stored key with a stray newline or control
        // character (the classic paste artefact) makes them throw FormatException — which would escape
        // as an unhandled 500 instead of the 502 this method carefully produces for upstream trouble.
        request.Headers.TryAddWithoutValidation("api-key", provider.ApiKey);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {provider.ApiKey}");

        HttpResponseMessage upstreamResponse;
        try
        {
            upstreamResponse = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Azure deployments lookup for /models failed");
            Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstreamResponse)
        {
            var body = await upstreamResponse.Content.ReadAsStringAsync(cancellationToken);
            Response.StatusCode = (int)upstreamResponse.StatusCode;
            Response.ContentType = "application/json";

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                // Surface Azure's own error verbatim.
                await Response.WriteAsync(body, cancellationToken);
                return;
            }

            await Response.WriteAsync(AzureDeploymentsToModelList(body), cancellationToken);
        }
    }

    // Reshape Azure's deployments response ({ "data": [ { "id", "model" } ] }) into an OpenAI
    // model list ({ "object": "list", "data": [ { "id", "object": "model" } ] }). Falls back to the
    // raw upstream body if it is not the expected JSON shape.
    private static string AzureDeploymentsToModelList(string deploymentsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(deploymentsJson);
            using var ms = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteString("object", "list");
            writer.WritePropertyName("data");
            writer.WriteStartArray();
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var id = item.TryGetProperty("id", out var idEl) &&
                             idEl.ValueKind == System.Text.Json.JsonValueKind.String
                        ? idEl.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    writer.WriteStartObject();
                    writer.WriteString("id", id);
                    writer.WriteString("object", "model");
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            return deploymentsJson;
        }
    }

    private async Task<ResolvedApiKey?> GetResolvedKeyAsync(string? projectSlug, CancellationToken cancellationToken)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var rawKey = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : null;

        if (string.IsNullOrEmpty(rawKey))
        {
            return null;
        }

        return await apiKeyResolver.ResolveAsync(rawKey, projectSlug, cancellationToken);
    }

    // ── Non-streaming ─────────────────────────────────────────────────────────

    private async Task ProxyBufferedResponseAsync(
        IModelProvider provider,
        IProject project,
        string requestBody,
        HttpResponseMessage upstreamResponse,
        Stopwatch sw,
        string? sessionId,
        string? conversationId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        // Stream the upstream body straight through to the client instead of materializing it as a
        // string and re-encoding it. The old ReadAsStringAsync + Encoding.UTF8.GetBytes held two
        // full-size copies resident per in-flight request with no bound at all on the response side
        // (the request side already caps at MaxRequestBodyBytes) — a large or hostile upstream reply
        // could push the proxy to OOM. The forwarded bytes are copied through verbatim and never
        // truncated; only the transcript we capture for ingestion is bounded at
        // MaxCapturedResponseChars, exactly as the streaming path does.
        var captured = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var buffer = ArrayPool<byte>.Shared.Rent(CopyChunkBytes);
        var chars = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(buffer.Length));

        try
        {
            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);

            int read;
            while ((read = await upstreamStream.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                // Bound the captured copy: decode this chunk and append only while under the cap. The
                // Decoder carries a multi-byte UTF-8 sequence split across a chunk boundary between
                // calls, so the captured text never gets a corrupted character at a seam.
                if (captured.Length < MaxCapturedResponseChars)
                {
                    var decoded = decoder.GetChars(buffer, 0, read, chars, 0);
                    captured.Append(chars, 0, Math.Min(decoded, MaxCapturedResponseChars - captured.Length));
                }
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(buffer);
            sw.Stop();

            // Capture is decoupled from the client request lifetime: the upstream call has already
            // completed, so a client disconnect/timeout here must not drop the captured call.
            // Publish with CancellationToken.None rather than the request-aborted token.
            await EnqueueSafeAsync(provider, project, requestBody, captured.ToString(), sw.Elapsed, upstreamResponse.StatusCode, sessionId, conversationId, agentName, CancellationToken.None);
        }
    }

    // ── Streaming (SSE) ───────────────────────────────────────────────────────

    private async Task ProxyStreamingResponseAsync(
        IModelProvider provider,
        IProject project,
        string requestBody,
        HttpResponseMessage upstreamResponse,
        Stopwatch sw,
        string? sessionId,
        string? conversationId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        var accumulated = new StringBuilder();

        await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(upstreamStream, Encoding.UTF8, leaveOpen: true);

        // Read fixed-size chunks and split on '\n' ourselves rather than calling ReadLineAsync, which
        // grows an unbounded internal buffer until it finds a newline. Whether the response is really
        // an event stream is decided by the *request* (`"stream": true`), never by the upstream, so a
        // provider that ignores the flag — or a WAF/error page in front of it — can answer with one
        // multi-megabyte single-line body, which ReadLineAsync would materialize whole as a string and
        // WriteSseSegmentAsync would then rent 3x its length on top of. A line that grows past
        // MaxForwardedLineChars is flushed as a raw, unterminated segment instead: the client still
        // receives every byte, but nothing unbounded is ever resident.
        var chunk = ArrayPool<char>.Shared.Rent(StreamReadChunkChars);
        var pending = new StringBuilder();

        try
        {
            int read;
            while ((read = await reader.ReadAsync(chunk.AsMemory(0, StreamReadChunkChars), cancellationToken)) > 0)
            {
                var start = 0;
                while (start < read)
                {
                    var newline = Array.IndexOf(chunk, '\n', start, read - start);
                    if (newline < 0)
                    {
                        pending.Append(chunk, start, read - start);
                        break;
                    }

                    pending.Append(chunk, start, newline - start);
                    start = newline + 1;
                    await EmitSseSegmentAsync(pending, accumulated, terminated: true, cancellationToken);
                }

                if (pending.Length >= MaxForwardedLineChars)
                {
                    await EmitSseSegmentAsync(pending, accumulated, terminated: false, cancellationToken);
                }
            }

            if (pending.Length > 0)
            {
                // Upstream ended without a trailing newline. The terminator is still written, exactly
                // as the previous ReadLineAsync-based loop did.
                await EmitSseSegmentAsync(pending, accumulated, terminated: true, cancellationToken);
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chunk);
            // Always publish what we accumulated, even when the client disconnects mid-stream: the
            // forward loop above throws out on disconnect, but the partial transcript is exactly the
            // data the proxy exists to capture. Decouple from the request-aborted token with
            // CancellationToken.None so the publish itself isn't cancelled by the same disconnect.
            sw.Stop();
            await EnqueueSafeAsync(provider, project, requestBody, accumulated.ToString(), sw.Elapsed, upstreamResponse.StatusCode, sessionId, conversationId, agentName, CancellationToken.None);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Drains the pending segment: appends it to the bounded capture buffer and forwards it to the
    // client. `terminated` distinguishes a real line (a '\n' was seen upstream, so one is written and
    // a trailing '\r' is dropped — the CRLF→LF normalization StreamReader.ReadLine used to do) from a
    // length-forced flush of an over-long line, which is written raw so the forwarded bytes stay
    // faithful and no newline is invented.
    private async Task EmitSseSegmentAsync(
        StringBuilder pending,
        StringBuilder accumulated,
        bool terminated,
        CancellationToken cancellationToken)
    {
        if (terminated && pending.Length > 0 && pending[^1] == '\r')
        {
            pending.Length--;
        }

        var segment = pending.ToString();
        pending.Clear();

        // Bound the captured copy (the forwarded bytes below are never truncated).
        if (accumulated.Length < MaxCapturedResponseChars)
        {
            accumulated.Append(segment, 0, Math.Min(segment.Length, MaxCapturedResponseChars - accumulated.Length));
            if (terminated && accumulated.Length < MaxCapturedResponseChars)
            {
                accumulated.Append('\n');
            }
        }

        await WriteSseSegmentAsync(segment, terminated, cancellationToken);
    }

    // Forwards one streamed segment (plus its '\n' terminator when the segment is a complete line)
    // and flushes so the token reaches the client immediately. Encodes into a pooled buffer to avoid
    // the per-line string concat and throwaway byte[] that a token-by-token completion would
    // otherwise allocate thousands of.
    private async Task WriteSseSegmentAsync(string segment, bool terminated, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(segment.Length) + 1);
        try
        {
            var count = Encoding.UTF8.GetBytes(segment, buffer);
            if (terminated)
            {
                buffer[count] = (byte)'\n';
                count++;
            }

            await Response.Body.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task EnqueueSafeAsync(
        IModelProvider provider,
        IProject project,
        string requestBody,
        string? responseBody,
        TimeSpan duration,
        HttpStatusCode httpStatus,
        string? sessionId,
        string? conversationId,
        string? agentName,
        CancellationToken cancellationToken,
        BlockedRequestMatch? blocked = null)
    {
        try
        {
            await stream.PublishAsync(
                new IngestMessage(
                    ProviderId: provider.Id,
                    ProjectId: project.Id,
                    RequestBody: requestBody,
                    ResponseBody: responseBody,
                    DurationMs: (long)duration.TotalMilliseconds,
                    HttpStatus: (int)httpStatus,
                    SessionId: sessionId,
                    AgentName: agentName,
                    BlockedByDetectorId: blocked?.DetectorId,
                    BlockedDetectorName: blocked?.DetectorName,
                    BlockedTriggerPattern: blocked?.TriggerPattern,
                    ConversationId: conversationId),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish ingestion message after proxy call");
        }
    }

    // ── Real-time blocking ────────────────────────────────────────────────────

    /// <summary>
    /// Rejects a request a blocking detector matched: 403 with an OpenAI-compatible error body (so
    /// SDKs surface a clean, non-retryable PermissionDenied error), then publishes the blocked call
    /// to the ingestion stream so it still appears as a (flagged) trace. The error names the
    /// detector but NEVER the matched excerpt — the excerpt may be the secret being protected. The
    /// same JSON doubles as the recorded ResponseBody: the ingestion parser reads error.message for
    /// non-2xx statuses, so the trace carries the block reason without parser changes.
    /// </summary>
    private async Task RejectBlockedRequestAsync(
        ResolvedApiKey resolved,
        string requestBody,
        BlockedRequestMatch blocked,
        TimeSpan elapsed,
        string? sessionId,
        string? conversationId,
        string? agentName,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Blocked request to project {ProjectId} by detector {DetectorId} ({DetectorName})",
            resolved.Project.Id, blocked.DetectorId, blocked.DetectorName);

        var errorJson = BuildBlockedErrorJson(blocked.DetectorName);

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsync(errorJson, cancellationToken);

        // CancellationToken.None: a client disconnect right after the 403 must not drop the record.
        await EnqueueSafeAsync(
            resolved.Provider,
            resolved.Project,
            requestBody,
            responseBody: errorJson,
            duration: elapsed,
            httpStatus: HttpStatusCode.Forbidden,
            sessionId: sessionId,
            conversationId: conversationId,
            agentName: agentName,
            CancellationToken.None,
            blocked: blocked);
    }

    private static string BuildBlockedErrorJson(string detectorName)
    {
        // Serialize via the JSON writer (not string interpolation) so a detector name containing
        // quotes cannot break out of the error payload.
        var payload = new
        {
            error = new
            {
                message = $"Request blocked by Proxytrace anomaly detector '{detectorName}'.",
                type = "invalid_request_error",
                param = (string?)null,
                code = "proxytrace_blocked",
            },
        };
        return System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private HttpRequestMessage BuildUpstreamRequest(string path, byte[] bodyBytes, string providerApiKey, Uri providerEndpoint)
    {
        var baseUrl = providerEndpoint.ToString().TrimEnd('/');
        var upstreamRequest = new HttpRequestMessage
        {
            Method = new HttpMethod(Request.Method),
            RequestUri = new Uri($"{baseUrl}/{path}{Request.QueryString}"),
            // Only carry a body when there is one — attaching empty content to a bodyless verb
            // (GET /models, DELETE, …) makes strict upstreams reject the request.
            Content = bodyBytes.Length > 0 ? new ByteArrayContent(bodyBytes) : null,
        };

        // The client's Connection header may name additional per-connection headers beyond the
        // fixed hop-by-hop set (RFC 9110 §7.6.1); those belong to the client↔proxy hop only.
        var connectionOwned = Request.Headers.Connection
            .SelectMany(value => (value ?? string.Empty).Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(token => token.ToLowerInvariant())
            .ToHashSet();

        foreach (var header in Request.Headers)
        {
            var key = header.Key.ToLowerInvariant();
            if (StrippedRequestHeaders.Contains(key)
                || connectionOwned.Contains(key)
                || key.StartsWith(ProxytraceHeaderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // Client-supplied values may be malformed; TryAddWithoutValidation forwards them raw
            // instead of letting a bad header crash the proxy with an opaque 500. Content headers
            // (Content-Type, …) are rejected by the request-header collection and attach to the
            // content instead; on a bodyless request they have no content to describe and are
            // dropped with it.
            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, (IEnumerable<string?>)header.Value);
            }
        }

        // Replace the client's Proxytrace API key with the model provider's actual key. Azure's
        // classic data-plane auth reads `api-key` rather than the bearer — send both, matching
        // ServeAzureModelsAsync and ProviderClient.
        upstreamRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {providerApiKey}");
        if (ProviderEndpoints.IsAzure(providerEndpoint))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("api-key", providerApiKey);
        }

        return upstreamRequest;
    }

    private static byte[] InjectStreamUsageOption(byte[] bodyBytes)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bodyBytes);
            if (doc.RootElement.TryGetProperty("stream_options", out _))
            {
                return bodyBytes;
            }

            using var ms = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(ms);
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                prop.WriteTo(writer);
            }
            writer.WritePropertyName("stream_options");
            writer.WriteStartObject();
            writer.WriteBoolean("include_usage", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
            return ms.ToArray();
        }
        catch
        {
            return bodyBytes;
        }
    }

    private static bool IsStreamingRequest(string requestBody, string? contentType)
    {
        if (!string.IsNullOrEmpty(contentType) &&
            contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("stream", out var stream))
            {
                return stream.ValueKind == System.Text.Json.JsonValueKind.True;
            }
        }
        catch { /* not JSON or no stream field */ }

        return false;
    }
}
