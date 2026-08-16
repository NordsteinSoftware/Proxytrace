using Proxytrace.Domain.Invite;
using Proxytrace.Domain.User;

namespace Proxytrace.Storage.Internal.Entities.Invite;

[StoredDomainEntity(typeof(IInvite))]
internal record InviteEntity : Entity
{
    /// <summary>
    /// Gets or sets the email.
    /// </summary>
    public required string Email { get; init; }
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    public required UserRole Role { get; init; }

    /// <summary>
    /// <see cref="IInvite.TokenHash"/> — SHA-256 of the redemption token (the token is verify-only,
    /// so only its hash is stored).
    /// </summary>
    public required string TokenHash { get; init; }
    /// <summary>
    /// Gets or sets the expires at.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
    /// <summary>
    /// Gets or sets the consumed at.
    /// </summary>
    public DateTimeOffset? ConsumedAt { get; init; }
    /// <summary>
    /// Gets or sets the invited by.
    /// </summary>
    public required Guid InvitedBy { get; init; }
}
