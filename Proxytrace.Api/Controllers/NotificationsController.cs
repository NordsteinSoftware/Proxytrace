using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proxytrace.Api.Auth;
using Proxytrace.Api.Dto.Notifications;
using Proxytrace.Api.Json;
using Proxytrace.Application.Streaming;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Api.Controllers;

/// <summary>
/// API controller for notifications operations.
/// </summary>
[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationRepository repository;
    private readonly INotificationBroadcaster broadcaster;
    private readonly NotificationDtoMapper mapper;
    private readonly IProjectAccessGuard accessGuard;
    private readonly INotification.CreateNew createNotification;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationsController"/> class.
    /// </summary>
    public NotificationsController(
        INotificationRepository repository,
        INotificationBroadcaster broadcaster,
        NotificationDtoMapper mapper,
        IProjectAccessGuard accessGuard,
        INotification.CreateNew createNotification)
    {
        this.repository = repository;
        this.broadcaster = broadcaster;
        this.mapper = mapper;
        this.accessGuard = accessGuard;
        this.createNotification = createNotification;
    }

    // A project notification is visible to that project's members (and admins); a global
    // (null-project) notification is system-wide and only admins may see or act on it.
    private async Task<bool> CanAccessNotificationAsync(Guid? notificationProjectId, CancellationToken cancellationToken)
    {
        if (notificationProjectId is { } pid)
            return await accessGuard.CanAccessProjectAsync(pid, cancellationToken);
        return await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken) is null;
    }

    /// <summary>
    /// Gets the all.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<NotificationDto>> GetAll(
        [FromQuery] Guid? projectId = null,
        [FromQuery] bool includeRead = true,
        CancellationToken cancellationToken = default)
    {
        var notifications = await repository.GetForScopeAsync(projectId, includeRead, cancellationToken);

        // Restrict non-admins to notifications of projects they belong to; never leak other tenants'
        // rows or global (admin-only) notifications. Admins (accessible == null) see everything.
        var accessible = await accessGuard.GetAccessibleProjectIdsAsync(cancellationToken);
        if (accessible is not null)
            notifications = notifications
                .Where(n => n.ProjectId is { } pid && accessible.Contains(pid))
                .ToList();

        return notifications.Select(mapper.ToDto).ToList();
    }

    // A deep link (`/notifications/{id}`, e.g. from a notification email) cannot always be resolved
    // from the list: GetAll hard-excludes dismissed rows and hides global rows from non-admins.
    // Out-of-scope and missing both return 404 — never 403, which would confirm the id exists.
    /// <summary>
    /// Gets.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<NotificationDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var notification = await repository.FindAsync(id, cancellationToken);
        if (notification is null)
            return NotFound();
        if (!await CanAccessNotificationAsync(notification.ProjectId, cancellationToken))
            return NotFound();

        return mapper.ToDto(notification);
    }

    /// <summary>
    /// Mark read.
    /// </summary>
    [HttpPatch("{id:guid}/read")]
    public async Task<ActionResult<NotificationDto>> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        if (!await CanAccessNotificationAsync(existing.ProjectId, cancellationToken))
            return NotFound();

        if (existing.Status == NotificationStatus.Dismissed)
            return Conflict(new { error = "Cannot mark a dismissed notification read." });

        var updated = await existing.MarkRead(cancellationToken);
        if (updated.Status != existing.Status)
            broadcaster.Publish(NotificationStatusChangedEvent.Create(updated));
        return Ok(mapper.ToDto(updated));
    }

    /// <summary>
    /// Dismiss.
    /// </summary>
    [HttpPatch("{id:guid}/dismiss")]
    public async Task<ActionResult<NotificationDto>> Dismiss(Guid id, CancellationToken cancellationToken)
    {
        var existing = await repository.FindAsync(id, cancellationToken);
        if (existing is null)
            return NotFound();
        if (!await CanAccessNotificationAsync(existing.ProjectId, cancellationToken))
            return NotFound();

        var updated = await existing.Dismiss(cancellationToken);
        if (updated.Status != existing.Status)
            broadcaster.Publish(NotificationStatusChangedEvent.Create(updated));
        return Ok(mapper.ToDto(updated));
    }

    /// <summary>
    /// Test-only: writes a notification directly, bypassing <c>INotificationService</c>'s
    /// de-duplication so a spec can seed several rows for one target, and can seed one whose
    /// target does not exist (the dangling-target case the detail drawer has to handle).
    /// </summary>
    [HttpPost("seed")]
    [TestOnlyEndpoint]
    public async Task<ActionResult<NotificationDto>> Seed(
        [FromBody] SeedNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var notification = await repository.AddAsync(
            createNotification(
                request.Kind,
                request.Severity,
                request.Title,
                request.Message,
                request.ProjectId,
                request.TargetKind,
                request.TargetId),
            cancellationToken);

        broadcaster.Publish(NotificationCreatedEvent.Create(notification));
        return mapper.ToDto(notification);
    }

    /// <summary>
    /// Stream.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream([FromQuery] Guid? projectId = null, CancellationToken cancellationToken = default)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // The broadcaster is global. Snapshot the caller's project scope (null == admin) so another
        // tenant's notification content never crosses the wire.
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

            if (evt.ProjectId is { } eventProject)
            {
                // Skip projects the caller isn't a member of, then honor any client-side narrowing.
                if (accessible is not null && !accessible.Contains(eventProject))
                    continue;
                if (projectId is { } requested && eventProject != requested)
                    continue;
            }
            else if (accessible is not null)
            {
                // Global (null-project) notifications are admin-only.
                continue;
            }

            string eventName = evt switch
            {
                NotificationCreatedEvent => "notification-created",
                NotificationStatusChangedEvent => "notification-status-changed",
                _ => "unknown",
            };
            var data = SseEventSerializer.Serialize(evt, evt.GetType());
            await Response.WriteAsync($"event: {eventName}\ndata: {data}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
