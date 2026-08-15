using System.Text.Json;
using Microsoft.Extensions.Logging;
using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Api.Middleware;

/// <summary>
/// Records an <see cref="AuditAction.AccessDenied"/> failure when a state-changing request
/// (POST/PUT/PATCH/DELETE) is rejected with <c>403 Forbidden</c> — an authenticated caller attempting
/// something beyond their permission (non-admin hitting an admin route, a member acting on another
/// project, a license-gated feature). The denial is attributed to the caller (their id was stashed at
/// authentication time), so probing and privilege-escalation attempts are visible in the audit log.
/// </summary>
/// <remarks>
/// Deliberate scope: only <c>403</c> is captured, not <c>401</c> — an unauthenticated request carries
/// no actor and overlaps the dedicated <see cref="AuditAction.LoginFailed"/> signal, so it would be
/// noise. Access checks that hide existence behind a <c>404</c> (the project access guard) are
/// indistinguishable from genuine 404s here and are intentionally not recorded. This middleware must
/// sit <em>before</em> <c>UseAuthorization</c> in the pipeline so it still runs when authorization
/// short-circuits the request with a 403.
/// </remarks>
internal sealed class AuditDeniedAccessMiddleware
{
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
    };

    private readonly RequestDelegate next;
    private readonly ILogger<Audit> audit;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditDeniedAccessMiddleware"/> class.
    /// </summary>
    public AuditDeniedAccessMiddleware(RequestDelegate next, ILogger<Audit> audit)
    {
        this.next = next;
        this.audit = audit;
    }

    /// <summary>
    /// Invokes asynchronously.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.StatusCode == StatusCodes.Status403Forbidden
            && MutatingMethods.Contains(context.Request.Method))
        {
            var path = context.Request.Path.Value ?? string.Empty;
            audit.LogAudit(
                AuditAction.AccessDenied, "HttpRequest",
                targetLabel: $"{context.Request.Method} {path}",
                details: JsonSerializer.Serialize(new
                {
                    method = context.Request.Method,
                    path,
                    status = context.Response.StatusCode,
                }),
                outcome: AuditOutcome.Failure);
        }
    }
}
