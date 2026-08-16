using Proxytrace.Domain.AuditLog;
using Microsoft.Extensions.Logging;

namespace Proxytrace.Application.AuditLog.Internal;

/// <summary>
/// Captures typed audit events onto the <see cref="IAuditChannel"/>. Only <see cref="AuditState"/>
/// payloads (produced by <see cref="AuditLogExtensions.LogAudit"/>) are captured — ordinary logs on
/// the audit category are ignored. The actor and event time are resolved synchronously here, on the
/// caller's thread, while the request context is still alive: the background writer has none.
/// </summary>
internal sealed class AuditChannelLogger : ILogger
{
    private readonly IAuditChannel channel;
    private readonly IAuditActorAccessor? actorAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditChannelLogger"/> class.
    /// </summary>
    public AuditChannelLogger(IAuditChannel channel, IAuditActorAccessor? actorAccessor)
    {
        this.channel = channel;
        this.actorAccessor = actorAccessor;
    }

    /// <summary>
    /// Begins the scope.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// Determines whether the enabled.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <summary>
    /// Logs.
    /// </summary>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (state is not AuditState audit)
        {
            return;
        }

        var actor = actorAccessor?.GetCurrentActor() ?? AuditActor.System;

        var entry = new AuditCapture(
            audit.Action,
            actor.Type,
            actor.UserId,
            actor.Email,
            actor.ApiKeyId,
            audit.ProjectId,
            audit.TargetType,
            audit.TargetId,
            audit.TargetLabel,
            audit.Details,
            audit.Outcome,
            DateTimeOffset.UtcNow);

        channel.TryWrite(entry);
    }
}
