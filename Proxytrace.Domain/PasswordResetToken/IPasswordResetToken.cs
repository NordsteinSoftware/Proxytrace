using Proxytrace.Domain.User;

namespace Proxytrace.Domain.PasswordResetToken;

/// <summary>
/// A single-use, short-lived token that lets a user set a new password without knowing the old one.
/// Issued by the self-service "forgot password" flow or by an admin. The raw token is surfaced once
/// (emailed, logged for the operator, or returned as an admin link) and only its hash is stored.
/// </summary>
public interface IPasswordResetToken : IDomainEntity<IPasswordResetToken>
{
    /// <summary>The user whose password this token can reset.</summary>
    IUser User { get; }

    /// <summary>
    /// SHA-256 hash of the reset token. The token is verify-only — the redeemer presents the raw
    /// token, which is hashed and compared — so only the hash is stored and it is unrecoverable.
    /// </summary>
    string TokenHash { get; }

    /// <summary>Moment after which the token can no longer be redeemed.</summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>When the token was redeemed, or <see langword="null"/> if still pending.</summary>
    DateTimeOffset? ConsumedAt { get; }

    bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    bool IsConsumed => ConsumedAt is not null;

    /// <summary>Marks the token as redeemed and persists.</summary>
    Task<IPasswordResetToken> MarkConsumedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Factory delegate for creating a new new instance.
    /// </summary>
    public delegate IPasswordResetToken CreateNew(IUser user, string tokenHash, DateTimeOffset expiresAt);
    /// <summary>
    /// Factory delegate for creating a new existing instance.
    /// </summary>
    public delegate IPasswordResetToken CreateExisting(IUser user, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset? consumedAt, IDomainEntityData existing);
}
