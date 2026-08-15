using Proxytrace.Domain.PasswordResetToken;

namespace Proxytrace.Storage.Internal.Entities.PasswordResetToken;

[StoredDomainEntity(typeof(IPasswordResetToken))]
internal record PasswordResetTokenEntity : Entity
{
    /// <summary>
    /// Gets or sets the user.
    /// </summary>
    public required Guid User { get; init; }

    /// <summary>
    /// <see cref="IPasswordResetToken.TokenHash"/> — SHA-256 of the reset token (verify-only, so only
    /// its hash is stored).
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
}
