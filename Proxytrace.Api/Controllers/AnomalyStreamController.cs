using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Json;
using Proxytrace.Application.Streaming;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// SSE stream of custom-anomaly flags. Global broadcaster, server-filtered per frame to the
/// caller's member projects — mirrors <c>AgentCallsController.Stream</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/anomalies")]
public class AnomalyStreamController : ControllerBase
{
    private readonly ICustomAnomalyBroadcaster broadcaster;
    private readonly IProjectAccessGuard accessGuard;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomalyStreamController"/> class.
    /// </summary>
    public AnomalyStreamController(
        ICustomAnomalyBroadcaster broadcaster,
        IProjectAccessGuard accessGuard)
    {
        this.broadcaster = broadcaster;
        this.accessGuard = accessGuard;
    }

    /// <summary>
    /// Stream.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // Snapshot the caller's project scope once; non-admins only receive anomalies for projects
        // they belong to (admins: accessible == null → all).
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);

        var reader = broadcaster.Subscribe(cancellationToken);

        // Route through the heartbeat reader so a quiet stream periodically writes a comment frame;
        // a half-open socket (which never raises RequestAborted) then fails the write and the
        // broadcaster's cancellation registration unsubscribes, instead of leaking the slot forever.
        await foreach (var evt in SseWriter.ReadWithHeartbeatAsync(reader, cancellationToken))
        {
            if (evt is null)
            {
                await SseWriter.WriteHeartbeatAsync(Response, cancellationToken);
                continue;
            }

            if (accessible is not null && !accessible.Contains(evt.ProjectId))
                continue;
            var data = SseEventSerializer.Serialize(evt);
            await Response.WriteAsync($"event: anomaly-flagged\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
