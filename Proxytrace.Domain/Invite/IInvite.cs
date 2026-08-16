using Proxytrace.Domain.User;

namespace Proxytrace.Domain.Invite;

/// <summary>
/// A single-use invite token issued by an admin to onboard a new local-auth user.
/// </summary>
public interface IInvite : IDomainEntity<IInvite>
{
    /// <summary>Email address the invite was issued to.</summary>
    string Email { get; }

    /// <summary>Role to grant the user when they redeem the invite.</summary>
    UserRole Role { get; }

    /// <summary>
    /// SHA-256 hash of the single-use redemption token. The token is verify-only — the redeemer
    /// presents the raw token, which is hashed and compared — so only the hash is stored. The raw
    /// token (and its invite link) is surfaced once at creation and is unrecoverable afterwards.
    /// </summary>
    string TokenHash { get; }

    /// <summary>Moment after which the invite can no longer be redeemed.</summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>When the invite was redeemed, or <see langword="null"/> if still pending.</summary>
    DateTimeOffset? ConsumedAt { get; }

    /// <summary>Admin user who created this invite.</summary>
    IUser InvitedBy { get; }

    bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    bool IsConsumed => ConsumedAt is not null;

    /// <summary>Marks the invite as redeemed and persists.</summary>
    Task<IInvite> MarkConsumedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IInvite CreateNew(string email, UserRole role, string tokenHash, DateTimeOffset expiresAt, IUser invitedBy);
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IInvite CreateExisting(string email, UserRole role, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset? consumedAt, IUser invitedBy, IDomainEntityData existing);
}
