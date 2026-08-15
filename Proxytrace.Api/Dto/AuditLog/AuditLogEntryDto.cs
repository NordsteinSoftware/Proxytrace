using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Api.Dto.AuditLog;

/// <summary>
/// Data transfer object representing a audit log entry.
/// </summary>
public record AuditLogEntryDto(
    Guid Id,
    AuditAction Action,
    AuditActorType ActorType,
    Guid? ActorUserId,
    string? ActorEmail,
    Guid? ActorApiKeyId,
    Guid? ProjectId,
    string TargetType,
    Guid? TargetId,
    string? TargetLabel,
    string? Details,
    AuditOutcome Outcome,
    DateTimeOffset CreatedAt);
