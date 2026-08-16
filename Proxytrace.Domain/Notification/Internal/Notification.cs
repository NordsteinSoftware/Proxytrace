using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;

namespace Proxytrace.Domain.Notification.Internal;

internal record Notification : DomainEntity<INotification>, INotification
{
    /// <summary>
    /// Gets the kind.
    /// </summary>
    public NotificationKind Kind { get; }
    /// <summary>
    /// Gets the severity.
    /// </summary>
    public NotificationSeverity Severity { get; }
    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// Gets the message.
    /// </summary>
    public string Message { get; }
    /// <summary>
    /// Gets or sets the status.
    /// </summary>
    public NotificationStatus Status { get; private init; }
    /// <summary>
    /// Gets the project id.
    /// </summary>
    public Guid? ProjectId { get; }
    /// <summary>
    /// Gets the target kind.
    /// </summary>
    public NotificationTargetKind? TargetKind { get; }
    /// <summary>
    /// Gets the target id.
    /// </summary>
    public Guid? TargetId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Notification"/> class.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="Notification"/> class.
    /// </summary>
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

    /// <summary>
    /// Mark read.
    /// </summary>
    public Task<INotification> MarkRead(CancellationToken cancellationToken = default)
    {
        if (Status == NotificationStatus.Read)
            return Task.FromResult<INotification>(this);

        if (Status != NotificationStatus.Unread)
            throw new InvalidOperationException($"Cannot mark notification {Id} read from status {Status}.");

        return ApplyAsync(this with { Status = NotificationStatus.Read }, cancellationToken);
    }

    /// <summary>
    /// Dismiss.
    /// </summary>
    public Task<INotification> Dismiss(CancellationToken cancellationToken = default)
    {
        if (Status == NotificationStatus.Dismissed)
            return Task.FromResult<INotification>(this);

        return ApplyAsync(this with { Status = NotificationStatus.Dismissed }, cancellationToken);
    }

    /// <summary>
    /// Validates.
    /// </summary>
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
