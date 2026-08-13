using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Notification.Internal;

internal record Notification : DomainEntity<INotification>, INotification
{
    public NotificationKind Kind { get; }
    public NotificationSeverity Severity { get; }
    public string Title { get; }
    public string Message { get; }
    public NotificationStatus Status { get; private init; }
    public Guid? ProjectId { get; }
    public NotificationTargetKind? TargetKind { get; }
    public Guid? TargetId { get; }

    public Notification(
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string message,
        Guid? projectId,
        NotificationTargetKind? targetKind,
        Guid? targetId,
        IRepository<INotification> repository) : base(repository)
    {
        Kind = kind;
        Severity = severity;
        Title = title;
        Message = message;
        Status = NotificationStatus.Unread;
        ProjectId = projectId;
        TargetKind = targetKind;
        TargetId = targetId;
    }

    public Notification(
        NotificationKind kind,
        NotificationSeverity severity,
        string title,
        string message,
        NotificationStatus status,
        Guid? projectId,
        NotificationTargetKind? targetKind,
        Guid? targetId,
        IDomainEntityData existing,
        IRepository<INotification> repository) : base(existing, repository)
    {
        Kind = kind;
        Severity = severity;
        Title = title;
        Message = message;
        Status = status;
        ProjectId = projectId;
        TargetKind = targetKind;
        TargetId = targetId;
    }

    public Task<INotification> MarkRead(CancellationToken cancellationToken = default)
    {
        if (Status == NotificationStatus.Read)
            return Task.FromResult<INotification>(this);

        if (Status != NotificationStatus.Unread)
            throw new InvalidOperationException($"Cannot mark notification {Id} read from status {Status}.");

        return ApplyAsync(this with { Status = NotificationStatus.Read }, cancellationToken);
    }

    public Task<INotification> Dismiss(CancellationToken cancellationToken = default)
    {
        if (Status == NotificationStatus.Dismissed)
            return Task.FromResult<INotification>(this);

        return ApplyAsync(this with { Status = NotificationStatus.Dismissed }, cancellationToken);
    }

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
            yield return result;

        yield return Validation.Defined(Kind);
        yield return Validation.Defined(Severity);
        yield return Validation.Defined(Status);
        yield return Validation.NotNullOrWhiteSpace(Title);
        yield return Validation.NotNullOrWhiteSpace(Message);

        // A target is a (kind, id) pair: both set or both null.
        if (TargetKind.HasValue != TargetId.HasValue)
        {
            yield return new ValidationResult(
                "TargetKind and TargetId must both be set or both be null.",
                [nameof(TargetKind), nameof(TargetId)]);
        }

        if (TargetKind.HasValue)
            yield return Validation.Defined(TargetKind.Value);

        if (TargetId.HasValue)
            yield return Validation.NotDefault(TargetId.Value);
    }
}
