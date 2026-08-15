using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Invite.Internal;

internal record Invite : DomainEntity<IInvite>, IInvite
{
    /// <summary>
    /// Gets the email.
    /// </summary>
    public string Email { get; }
    /// <summary>
    /// Gets the role.
    /// </summary>
    public UserRole Role { get; }
    /// <summary>
    /// Gets the token hash.
    /// </summary>
    public string TokenHash { get; }
    /// <summary>
    /// Gets the expires at.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; }
    /// <summary>
    /// Gets or sets the consumed at.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; private init; }
    /// <summary>
    /// Gets the invited by.
    /// </summary>
    public IUser InvitedBy { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Invite"/> class.
    /// </summary>
    public Invite(
        string email,
        UserRole role,
        string tokenHash,
        DateTimeOffset expiresAt,
        IUser invitedBy,
        IRepository<IInvite> repository) : base(repository)
    {
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        InvitedBy = invitedBy;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Invite"/> class.
    /// </summary>
    public Invite(
        string email,
        UserRole role,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset? consumedAt,
        IUser invitedBy,
        IDomainEntityData existing,
        IRepository<IInvite> repository) : base(existing, repository)
    {
        Email = email;
        Role = role;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        ConsumedAt = consumedAt;
        InvitedBy = invitedBy;
    }

    /// <summary>
    /// Mark consumed asynchronously.
    /// </summary>
    public Task<IInvite> MarkConsumedAsync(CancellationToken cancellationToken = default)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"Invite {Id} has already been consumed.");
        }
        return ApplyAsync(this with { ConsumedAt = DateTimeOffset.UtcNow }, cancellationToken);
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
        yield return Validation.NotNullOrWhiteSpace(TokenHash);
        yield return Validation.Defined(Role);
        yield return Validation.NotBefore(ExpiresAt, CreatedAt);

        foreach (var result in InvitedBy.Validate(validationContext))
        {
            yield return result;
        }
    }
}
