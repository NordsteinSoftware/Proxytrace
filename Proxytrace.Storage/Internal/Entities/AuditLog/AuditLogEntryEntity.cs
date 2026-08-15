using Proxytrace.Domain.AuditLog;

namespace Proxytrace.Storage.Internal.Entities.AuditLog;

[StoredDomainEntity(typeof(IAuditLogEntry))]
internal record AuditLogEntryEntity : Entity
{
    /// <summary>
    /// Gets or sets the action.
    /// </summary>
    public required AuditAction Action { get; init; }

    /// <summary>
    /// Gets or sets the actor type.
    /// </summary>
    public required AuditActorType ActorType { get; init; }

    /// <summary>
    /// Gets or sets the actor user id.
    /// </summary>
    public Guid? ActorUserId { get; init; }

    /// <summary>
    /// Gets or sets the actor email.
    /// </summary>
    public string? ActorEmail { get; init; }

    /// <summary>
    /// Gets or sets the actor api key id.
    /// </summary>
    public Guid? ActorApiKeyId { get; init; }

    /// <summary>
    /// Gets or sets the project id.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Gets or sets the target type.
    /// </summary>
    public required string TargetType { get; init; }

    /// <summary>
    /// Gets or sets the target id.
    /// </summary>
    public Guid? TargetId { get; init; }

    /// <summary>
    /// Gets or sets the target label.
    /// </summary>
    public string? TargetLabel { get; init; }

    /// <summary>
    /// Gets or sets the details.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets or sets the outcome.
    /// </summary>
    public required AuditOutcome Outcome { get; init; }
}
