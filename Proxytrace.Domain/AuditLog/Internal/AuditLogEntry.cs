using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.AuditLog.Internal;

internal record AuditLogEntry : DomainEntity<IAuditLogEntry>, IAuditLogEntry
{
    /// <summary>
    /// Gets the action.
    /// </summary>
    public AuditAction Action { get; }
    /// <summary>
    /// Gets the actor type.
    /// </summary>
    public AuditActorType ActorType { get; }
    /// <summary>
    /// Gets the actor user id.
    /// </summary>
    public Guid? ActorUserId { get; }
    /// <summary>
    /// Gets the actor email.
    /// </summary>
    public string? ActorEmail { get; }
    /// <summary>
    /// Gets the actor api key id.
    /// </summary>
    public Guid? ActorApiKeyId { get; }
    /// <summary>
    /// Gets the project id.
    /// </summary>
    public Guid? ProjectId { get; }
    /// <summary>
    /// Gets the target type.
    /// </summary>
    public string TargetType { get; }
    /// <summary>
    /// Gets the target id.
    /// </summary>
    public Guid? TargetId { get; }
    /// <summary>
    /// Gets the target label.
    /// </summary>
    public string? TargetLabel { get; }
    /// <summary>
    /// Gets the details.
    /// </summary>
    public string? Details { get; }
    /// <summary>
    /// Gets the outcome.
    /// </summary>
    public AuditOutcome Outcome { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogEntry"/> class.
    /// </summary>
    public AuditLogEntry(
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
        IRepository<IAuditLogEntry> repository) : base(repository)
    {
        Action = action;
        ActorType = actorType;
        ActorUserId = actorUserId;
        ActorEmail = actorEmail;
        ActorApiKeyId = actorApiKeyId;
        ProjectId = projectId;
        TargetType = targetType;
        TargetId = targetId;
        TargetLabel = targetLabel;
        Details = details;
        Outcome = outcome;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditLogEntry"/> class.
    /// </summary>
    public AuditLogEntry(
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
        IDomainEntityData existing,
        IRepository<IAuditLogEntry> repository) : base(existing, repository)
    {
        Action = action;
        ActorType = actorType;
        ActorUserId = actorUserId;
        ActorEmail = actorEmail;
        ActorApiKeyId = actorApiKeyId;
        ProjectId = projectId;
        TargetType = targetType;
        TargetId = targetId;
        TargetLabel = targetLabel;
        Details = details;
        Outcome = outcome;
    }

    /// <summary>
    /// Validates.
    /// </summary>
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        yield return Validation.Defined(Action);
        yield return Validation.Defined(ActorType);
        yield return Validation.Defined(Outcome);
        yield return Validation.NotNullOrWhiteSpace(TargetType);
    }
}
