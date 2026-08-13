using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Proxytrace.Domain.Internal;

namespace Proxytrace.Domain.AuditLog.Internal;

internal record AuditLogEntry : DomainEntity<IAuditLogEntry>, IAuditLogEntry
{
    public AuditAction Action { get; }
    public AuditActorType ActorType { get; }
    public Guid? ActorUserId { get; }
    public string? ActorEmail { get; }
    public Guid? ActorApiKeyId { get; }
    public Guid? ProjectId { get; }
    public string TargetType { get; }
    public Guid? TargetId { get; }
    public string? TargetLabel { get; }
    public string? Details { get; }
    public AuditOutcome Outcome { get; }

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
