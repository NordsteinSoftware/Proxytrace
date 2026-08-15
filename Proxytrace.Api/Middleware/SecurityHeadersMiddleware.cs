namespace Proxytrace.Api.Middleware;

/// <summary>
/// Adds security headers to every response. Matters most for the SPA the API serves
/// from wwwroot (the nginx deployment sets equivalent headers in nginx.conf). The CSP
/// mirrors frontend/index.html's build-time meta so both serve paths are consistent.
/// </summary>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate next;
    private readonly SecurityHeadersOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.
    /// </summary>
    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions options)
    {
        this.next = next;
        this.options = options;
    }

    /// <summary>
    /// Invokes asynchronously.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        // Skip CSP for Swagger UI, which relies on inline scripts/styles (Development only).
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            // no CSP
        }
        else if (context.Request.Path.StartsWithSegments("/docs"))
        {
            // The bundled VitePress manual at /docs emits a few static inline scripts (dark-mode
            // probe, page hash map). These are trusted, build-time content, so /docs gets a CSP
            // that allows inline scripts while the rest of the app keeps the strict policy.
            headers["Content-Security-Policy"] = options.DocsContentSecurityPolicy;
        }
        else
        {
            headers["Content-Security-Policy"] = options.ContentSecurityPolicy;
        }

        return next(context);
    }
}
