using System.ComponentModel.DataAnnotations;
using System.Text;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.UserTotpEnrollment.Internal;

internal record UserTotpEnrollment : DomainEntity<IUserTotpEnrollment>, IUserTotpEnrollment
{
    /// <summary>
    /// Gets the user.
    /// </summary>
    public IUser User { get; }
    /// <summary>
    /// Gets the secret.
    /// </summary>
    public string Secret { get; }
    /// <summary>
    /// Gets or sets the confirmed at.
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; private init; }
    /// <summary>
    /// Gets or sets the last used step.
    /// </summary>
    public long? LastUsedStep { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserTotpEnrollment"/> class.
    /// </summary>
    public UserTotpEnrollment(
        IUser user,
        string secret,
        IRepository<IUserTotpEnrollment> repository) : base(repository)
    {
        User = user;
        Secret = secret;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserTotpEnrollment"/> class.
    /// </summary>
    public UserTotpEnrollment(
        IUser user,
        string secret,
        DateTimeOffset? confirmedAt,
        long? lastUsedStep,
        IDomainEntityData existing,
        IRepository<IUserTotpEnrollment> repository) : base(existing, repository)
    {
        User = user;
        Secret = secret;
        ConfirmedAt = confirmedAt;
        LastUsedStep = lastUsedStep;
    }

    /// <summary>
    /// Confirm.
    /// </summary>
    public Task<IUserTotpEnrollment> Confirm(long usedStep, CancellationToken cancellationToken = default)
        => ApplyAsync(this with { ConfirmedAt = DateTimeOffset.UtcNow, LastUsedStep = usedStep }, cancellationToken);

    /// <summary>
    /// Record used step.
    /// </summary>
    public Task<IUserTotpEnrollment> RecordUsedStep(long step, CancellationToken cancellationToken = default)
    {
        // Single-use guard: a TOTP step may only ever move forward, so replaying a step at or
        // below the last one used is rejected. The application layer already only records steps
        // newer than LastUsedStep (TotpService.TryVerify), so this is belt-and-suspenders.
        if (LastUsedStep is { } lastUsedStep && step <= lastUsedStep)
        {
            throw new InvalidOperationException(
                $"Cannot record TOTP step {step} for enrollment {Id}: it must be newer than the last used step {lastUsedStep}.");
        }
        return ApplyAsync(this with { LastUsedStep = step }, cancellationToken);
    }

    // Redact the TOTP shared secret from the record's generated ToString()/PrintMembers so it never
    // leaks into a log line, exception message or debugger string — anyone holding it can mint valid
    // authenticator codes for this user indefinitely. Secret stays a public member (the verifier
    // reads it; equality keeps it) — only its textual rendering is masked.
    protected override bool PrintMembers(StringBuilder builder)
    {
        if (base.PrintMembers(builder))
        {
            builder.Append(", ");
        }

        builder.Append("User = ").Append(User)
            .Append(", Secret = ***")
            .Append(", ConfirmedAt = ").Append(ConfirmedAt)
            .Append(", LastUsedStep = ").Append(LastUsedStep);
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

        yield return Validation.NotNullOrWhiteSpace(Secret);

        foreach (var result in User.Validate(validationContext))
        {
            yield return result;
        }
    }
}
