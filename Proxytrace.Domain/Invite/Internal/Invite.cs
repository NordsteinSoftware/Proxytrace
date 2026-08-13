using System.ComponentModel.DataAnnotations;
using Nordstein.Core.Common.Validation;
using Nordstein.Core.Domain;
using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Invite.Internal;

internal record Invite : DomainEntity<IInvite>, IInvite
{
    public string Email { get; }
    public UserRole Role { get; }
    public string TokenHash { get; }
    public DateTimeOffset ExpiresAt { get; }
    public DateTimeOffset? ConsumedAt { get; private init; }
    public IUser InvitedBy { get; }

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

    public Task<IInvite> MarkConsumedAsync(CancellationToken cancellationToken = default)
    {
        if (ConsumedAt is not null)
        {
            throw new InvalidOperationException($"Invite {Id} has already been consumed.");
        }
        return ApplyAsync(this with { ConsumedAt = DateTimeOffset.UtcNow }, cancellationToken);
    }

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
