namespace Proxytrace.Domain.AuditLog;

/// <summary>
/// A captured audit-log entry — one auditable system action (who did what, to which target, in
/// which project) persisted for the Audit Log UI. Immutable: actions are recorded once and never
/// mutated. Actor/project/target references are stored as denormalized snapshots (plain ids + label)
/// with no foreign keys, so a recorded action survives deletion of the user/project/key/target it
/// refers to (e.g. the "project deleted" row outlives the project).
/// </summary>
public interface IAuditLogEntry : IDomainEntity<IAuditLogEntry>
{
    /// <summary>The kind of action that was performed.</summary>
    AuditAction Action { get; }

    /// <summary>Whether the action was performed by a user, an API key, or the system.</summary>
    AuditActorType ActorType { get; }

    /// <summary>The acting user's id (the key owner for API-key actions), or <see langword="null"/> for system actions.</summary>
    Guid? ActorUserId { get; }

    /// <summary>The acting user's email snapshot, or <see langword="null"/> when unknown (e.g. API-key actions) / system.</summary>
    string? ActorEmail { get; }

    /// <summary>The API key id when the action was authenticated with one, otherwise <see langword="null"/>.</summary>
    Guid? ActorApiKeyId { get; }

    /// <summary>The project the action belongs to, or <see langword="null"/> for instance-wide (global) actions.</summary>
    Guid? ProjectId { get; }

    /// <summary>The type of entity acted upon, e.g. <c>nameof(IApiKey)</c>.</summary>
    string TargetType { get; }

    /// <summary>The id of the entity acted upon, or <see langword="null"/> when there is no single target.</summary>
    Guid? TargetId { get; }

    /// <summary>A human-readable label snapshot for the target (survives the target's deletion).</summary>
    string? TargetLabel { get; }

    /// <summary>Action-specific structured context, serialized as JSON, or <see langword="null"/>.</summary>
    string? Details { get; }

    /// <summary>Whether the action succeeded.</summary>
    AuditOutcome Outcome { get; }

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IAuditLogEntry CreateNew(
        AuditAction action,
        AuditActorType actorType,
        Guid? actorUserId,
        string? actorEmail,
        Guid? actorApiKeyId,
        Guid? projectId,
        string targetType,
        Guid? targetId,
        string? targetLabel,
        string? details,
        AuditOutcome outcome);

    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IAuditLogEntry CreateExisting(
        AuditAction action,
        AuditActorType actorType,
        Guid? actorUserId,
        string? actorEmail,
        Guid? actorApiKeyId,
        Guid? projectId,
        string targetType,
        Guid? targetId,
        string? targetLabel,
        string? details,
        AuditOutcome outcome,
        IDomainEntityData existing);
}
