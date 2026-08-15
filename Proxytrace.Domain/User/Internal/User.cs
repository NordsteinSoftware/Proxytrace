using System.ComponentModel.DataAnnotations;
using System.Text;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.Notification;

namespace Proxytrace.Domain.User.Internal;

internal record User : DomainEntity<IUser>, IUser
{
    /// <summary>
    /// Gets the email.
    /// </summary>
    public string Email { get; }
    /// <summary>
    /// Gets the external subject.
    /// </summary>
    public string? ExternalSubject { get; }
    /// <summary>
    /// Gets or sets the password hash.
    /// </summary>
    public string? PasswordHash { get; private init; }
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public UserRole Role { get; private init; }
    /// <summary>
    /// Gets or sets the language.
    /// </summary>
    public string Language { get; private init; }
    /// <summary>
    /// Gets or sets the email notifications enabled.
    /// </summary>
    public bool EmailNotificationsEnabled { get; private init; }
    /// <summary>
    /// Gets or sets the email notification min severity.
    /// </summary>
    public NotificationSeverity EmailNotificationMinSeverity { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    public User(
        string email,
        string? externalSubject,
        string? passwordHash,
        UserRole role,
        string language,
        bool emailNotificationsEnabled,
        NotificationSeverity emailNotificationMinSeverity,
        IRepository<IUser> repository) : base(repository)
    {
        // Normalize at the write boundary (trimmed, invariant-lowercase) so the stored value is
        // always canonical. This lets the plain unique index on Email act case-insensitively and
        // keeps the login lookup an exact, index-served match (see UserRepository.FindByEmailAsync).
        Email = email.Trim().ToLowerInvariant();
        ExternalSubject = externalSubject;
        PasswordHash = passwordHash;
        Role = role;
        Language = language;
        EmailNotificationsEnabled = emailNotificationsEnabled;
        EmailNotificationMinSeverity = emailNotificationMinSeverity;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    public User(
        string email,
        string? externalSubject,
        string? passwordHash,
        UserRole role,
        string language,
        bool emailNotificationsEnabled,
        NotificationSeverity emailNotificationMinSeverity,
        IDomainEntityData existing,
        IRepository<IUser> repository) : base(existing, repository)
    {
        // Normalize on rehydration too: any re-save of a row that predates the backfill then
        // persists the canonical form, and in-memory comparisons stay consistent.
        Email = email.Trim().ToLowerInvariant();
        ExternalSubject = externalSubject;
        PasswordHash = passwordHash;
        Role = role;
        Language = language;
        EmailNotificationsEnabled = emailNotificationsEnabled;
        EmailNotificationMinSeverity = emailNotificationMinSeverity;
    }

    /// <summary>
    /// Change role.
    /// </summary>
    public Task<IUser> ChangeRole(UserRole role, CancellationToken cancellationToken = default)
        => Role == role
            ? Task.FromResult<IUser>(this)
            : ApplyAsync(this with { Role = role }, cancellationToken);

    /// <summary>
    /// Change password hash.
    /// </summary>
    public Task<IUser> ChangePasswordHash(string passwordHash, CancellationToken cancellationToken = default)
        => ApplyAsync(this with { PasswordHash = passwordHash }, cancellationToken);

    /// <summary>
    /// Change language.
    /// </summary>
    public Task<IUser> ChangeLanguage(string language, CancellationToken cancellationToken = default)
        => Language == language
            ? Task.FromResult<IUser>(this)
            : ApplyAsync(this with { Language = language }, cancellationToken);

    /// <summary>
    /// Change email notification preferences.
    /// </summary>
    public Task<IUser> ChangeEmailNotificationPreferences(bool emailNotificationsEnabled, NotificationSeverity emailNotificationMinSeverity, CancellationToken cancellationToken = default)
        => EmailNotificationsEnabled == emailNotificationsEnabled && EmailNotificationMinSeverity == emailNotificationMinSeverity
            ? Task.FromResult<IUser>(this)
            : ApplyAsync(this with { EmailNotificationsEnabled = emailNotificationsEnabled, EmailNotificationMinSeverity = emailNotificationMinSeverity }, cancellationToken);

    // Redact the stored password hash from the record's generated ToString()/PrintMembers so it never
    // leaks into a log line, exception message or debugger string — a salted, slow hash lifted from
    // the operator Error Log or a support bundle is an offline-cracking target. It also travels
    // transitively: any record holding an IUser (UserTotpEnrollment, ApiKey.Owner) prints it. The
    // same treatment ModelProvider gives its upstream API key. PasswordHash stays a public member
    // (the login path reads it; equality keeps it) — only its textual rendering is masked.
    // ExternalSubject is deliberately left visible: it is an identifier, not a credential, and
    // holding it grants nothing (like EmailSettings.Username, which also stays).
    protected override bool PrintMembers(StringBuilder builder)
    {
        if (base.PrintMembers(builder))
        {
            builder.Append(", ");
        }

        builder.Append("Email = ").Append(Email)
            .Append(", ExternalSubject = ").Append(ExternalSubject)
            .Append(", PasswordHash = ***")
            .Append(", Role = ").Append(Role)
            .Append(", Language = ").Append(Language)
            .Append(", EmailNotificationsEnabled = ").Append(EmailNotificationsEnabled)
            .Append(", EmailNotificationMinSeverity = ").Append(EmailNotificationMinSeverity);
        return true;
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

        yield return Validation.NotNullOrWhiteSpace(Email);
        yield return Validation.Defined(Role);
        yield return Validation.Defined(EmailNotificationMinSeverity);

        yield return Validation.NotNullOrWhiteSpace(Language);
        if (!SupportedLanguages.IsSupported(Language))
        {
            yield return new ValidationResult(
                $"Language '{Language}' is not a supported UI language.",
                [nameof(Language)]);
        }

        if (string.IsNullOrWhiteSpace(ExternalSubject) && string.IsNullOrWhiteSpace(PasswordHash))
        {
            yield return new ValidationResult(
                "User must have either ExternalSubject (OIDC) or PasswordHash (local).",
                [nameof(ExternalSubject), nameof(PasswordHash)]);
        }
    }
}
