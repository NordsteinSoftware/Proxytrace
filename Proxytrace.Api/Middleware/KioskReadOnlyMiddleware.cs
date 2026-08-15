using System.Text.Json;
using Proxytrace.Domain.Kiosk;

namespace Proxytrace.Api.Kiosk;

internal sealed class KioskReadOnlyMiddleware
{
    private static readonly HashSet<string> ReadMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", 
        "HEAD", 
        "OPTIONS"
    };

    private readonly RequestDelegate next;
    private readonly KioskOptions options;
    private readonly KioskEndpointOptions endpoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="KioskReadOnlyMiddleware"/> class.
    /// </summary>
    public KioskReadOnlyMiddleware(RequestDelegate next, KioskOptions options, KioskEndpointOptions endpoint)
    {
        this.next = next;
        this.options = options;
        this.endpoint = endpoint;
    }

    /// <summary>
    /// Invokes asynchronously.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Enabled)
        {
            await next(context);
            return;
        }
        
        var method = context.Request.Method;
        if (!ReadMethods.Contains(method) && !IsAllowedWrite())
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            var payload = JsonSerializer.Serialize(new
            {
                kiosk = true,
                code = "READ_ONLY",
                message = "Demo mode is read-only.",
            });
            await context.Response.WriteAsync(payload);
            return;
        }

        await next(context);
    }

    // When a real LLM endpoint is configured, kiosk becomes a fully interactive single-user
    // instance: lift the read-only restriction entirely. Without an endpoint the demo stays
    // read-only (and the Tracey write path is unreachable anyway, since it needs the endpoint).
    private bool IsAllowedWrite() => endpoint.IsConfigured;
}
