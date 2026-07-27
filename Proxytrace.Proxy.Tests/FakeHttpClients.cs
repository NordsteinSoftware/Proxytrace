using System.Net;
using System.Text;

namespace Proxytrace.Proxy.Tests;

/// <summary>Returns a fixed response body (and optional response headers) for every request.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string responseBody;
    private readonly HttpStatusCode statusCode;
    private readonly IReadOnlyDictionary<string, string> responseHeaders;

    public FakeHttpMessageHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        IReadOnlyDictionary<string, string>? responseHeaders = null)
    {
        this.responseBody = responseBody;
        this.statusCode = statusCode;
        this.responseHeaders = responseHeaders ?? new Dictionary<string, string>();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
        foreach (var (key, value) in responseHeaders)
        {
            response.Headers.TryAddWithoutValidation(key, value);
        }

        return Task.FromResult(response);
    }

    public static string BuildOpenAiResponse(string assistantText)
    {
        var encoded = System.Text.Json.JsonSerializer.Serialize(assistantText);
        return
            "{\"id\":\"chatcmpl-test\",\"object\":\"chat.completion\",\"model\":\"gpt-4o\"," +
            "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" +
            encoded +
            "},\"finish_reason\":\"stop\"}]," +
            "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
    }
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient client;

    public FakeHttpClientFactory(string responseBody)
        => client = new HttpClient(new FakeHttpMessageHandler(responseBody))
        {
            BaseAddress = new Uri("http://fake-upstream/"),
        };

    public HttpClient CreateClient(string name) => client;
}

internal sealed class ThrowingHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new ThrowingHandler())
    {
        BaseAddress = new Uri("http://fake-upstream/"),
    };

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }
}

/// <summary>Records the request the proxy forwarded upstream so tests can assert on it.</summary>
internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    private readonly string responseBody;

    public CapturingHttpMessageHandler(string responseBody = "{}") => this.responseBody = responseBody;

    public HttpMethod? LastMethod { get; private set; }
    public bool LastHadContent { get; private set; }
    public byte[] LastBody { get; private set; } = [];
    public string? LastContentType { get; private set; }
    public Uri? LastUri { get; private set; }
    public string? LastAuthorization { get; private set; }

    /// <summary>Every header of the forwarded request (request + content headers), lowercase keys.</summary>
    public IReadOnlyDictionary<string, string> LastHeaders { get; private set; } =
        new Dictionary<string, string>();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastMethod = request.Method;
        LastUri = request.RequestUri;
        LastAuthorization = request.Headers.TryGetValues("Authorization", out var auth)
            ? string.Join(",", auth)
            : null;

        var headers = request.Headers.AsEnumerable();
        if (request.Content is not null)
        {
            headers = headers.Concat(request.Content.Headers);
        }

        LastHeaders = headers.ToDictionary(
            h => h.Key.ToLowerInvariant(),
            h => string.Join(",", h.Value));

        LastHadContent = request.Content is not null;
        if (request.Content is not null)
        {
            LastBody = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            LastContentType = request.Content.Headers.TryGetValues("Content-Type", out var values)
                ? string.Join(",", values)
                : null;
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}

internal sealed class SingleHandlerClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler handler;
    public SingleHandlerClientFactory(HttpMessageHandler handler) => this.handler = handler;
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Serves a fixed byte body whose stream hands out at most <c>maxBytesPerRead</c> bytes per read, so
/// tests can force the buffered proxy path across many chunk boundaries (and split a multi-byte UTF-8
/// character across two reads) deterministically.
/// </summary>
internal sealed class ChunkedRawHttpClientFactory : IHttpClientFactory
{
    private readonly byte[] body;
    private readonly int maxBytesPerRead;

    public ChunkedRawHttpClientFactory(byte[] body, int maxBytesPerRead)
    {
        this.body = body;
        this.maxBytesPerRead = maxBytesPerRead;
    }

    public HttpClient CreateClient(string name)
        => new(new Handler(body, maxBytesPerRead)) { BaseAddress = new Uri("http://fake-upstream/") };

    private sealed class Handler : HttpMessageHandler
    {
        private readonly byte[] body;
        private readonly int maxBytesPerRead;

        public Handler(byte[] body, int maxBytesPerRead)
        {
            this.body = body;
            this.maxBytesPerRead = maxBytesPerRead;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ChunkLimitedStream(body, maxBytesPerRead)),
            });
    }
}

/// <summary>A read-only stream over a byte[] that never returns more than <c>maxChunk</c> bytes per read.</summary>
internal sealed class ChunkLimitedStream : Stream
{
    private readonly byte[] data;
    private readonly int maxChunk;
    private int position;

    public ChunkLimitedStream(byte[] data, int maxChunk)
    {
        this.data = data;
        this.maxChunk = maxChunk;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = data.Length - position;
        if (remaining <= 0)
        {
            return 0;
        }

        var n = Math.Min(Math.Min(count, maxChunk), remaining);
        Array.Copy(data, position, buffer, offset, n);
        position += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A response stream that records everything written to it plus the size of the largest single write,
/// so a test can assert the proxy forwarded a body in bounded pieces instead of materializing it whole.
/// </summary>
internal sealed class RecordingResponseStream : Stream
{
    private readonly MemoryStream written = new();

    public int LargestWriteBytes { get; private set; }

    public byte[] Written => written.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => written.Length;
    public override long Position { get => written.Position; set => throw new NotSupportedException(); }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        LargestWriteBytes = Math.Max(LargestWriteBytes, buffer.Length);
        await written.WriteAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        LargestWriteBytes = Math.Max(LargestWriteBytes, count);
        written.Write(buffer, offset, count);
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// A read-only stream that produces <c>length</c> bytes of filler without materializing them, and
/// counts how many were actually consumed. Lets a test drive the proxy's request-body cap with a body
/// far larger than the cap and then assert the proxy stopped reading at the cap.
/// </summary>
internal sealed class GeneratedByteStream : Stream
{
    private readonly long length;
    private long position;

    public GeneratedByteStream(long length) => this.length = length;

    /// <summary>Bytes the consumer actually pulled — never the full length once the cap bites.</summary>
    public long BytesRead => position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = length - position;
        if (remaining <= 0)
        {
            return 0;
        }

        var n = (int)Math.Min(count, remaining);
        Array.Fill(buffer, (byte)'a', offset, n);
        position += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A response stream that fails every write — simulates a client that disconnected.</summary>
internal sealed class ThrowOnWriteStream : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => throw new IOException("client disconnected");

    public override void Write(byte[] buffer, int offset, int count)
        => throw new IOException("client disconnected");

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
